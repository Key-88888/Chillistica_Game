using Chillistica_game.Service;

HostApplicationBuilder builder =
    Host.CreateApplicationBuilder(args);

builder.Services.AddWindowsService(options =>
{
    options.ServiceName =
        "Chillistica_game Service";
});

builder.Services.AddSingleton<ServiceLogger>();
builder.Services.AddHostedService<Worker>();
builder.Services.AddHostedService<NamedPipeServer>();

IHost host =
    builder.Build();

await host.RunAsync();

