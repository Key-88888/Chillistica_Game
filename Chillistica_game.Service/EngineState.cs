namespace Chillistica_game.Service;

public sealed class EngineState
{
    private const int Stopped = 0;
    private const int Running = 1;

    private int _state = Stopped;

    public bool IsRunning =>
        Volatile.Read(ref _state) == Running;

    public string GetStatus()
    {
        return IsRunning
            ? "ENGINE_RUNNING"
            : "ENGINE_STOPPED";
    }

    public string Start()
    {
        int previousState =
            Interlocked.CompareExchange(
                ref _state,
                Running,
                Stopped);

        return previousState == Stopped
            ? "ENGINE_STARTED"
            : "ENGINE_ALREADY_RUNNING";
    }

    public string Stop()
    {
        int previousState =
            Interlocked.CompareExchange(
                ref _state,
                Stopped,
                Running);

        return previousState == Running
            ? "ENGINE_STOPPED"
            : "ENGINE_ALREADY_STOPPED";
    }
}
