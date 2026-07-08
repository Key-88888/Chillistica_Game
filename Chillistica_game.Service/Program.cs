using Chillistica_game.Service;
using Microsoft.Extensions.Options;

HostApplicationBuilder builder =
    Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName =
        "Chillistica_game Service";
});

EngineOptions engineOptions =
    EngineProfileLoader.LoadOrFallback(
        builder.Configuration);

if (!string.IsNullOrWhiteSpace(engineOptions.ConfigurationWarning))
{
    new ServiceLogger().Error(
        stage: "EngineProfileLoad",
        exception: new InvalidOperationException(
            engineOptions.ConfigurationWarning));
}

builder.Services.AddSingleton<IOptions<EngineOptions>>(
    Options.Create(
        engineOptions));

builder.Services.AddSingleton<ServiceLogger>();
builder.Services.AddSingleton<EngineProcessManager>();

builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<NamedPipeServer>();

IHost host =
    builder.Build();

await host.RunAsync();
