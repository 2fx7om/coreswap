namespace CoreSwap.Services;

public static class AppServices
{
    public static SessionService Session { get; } = new();
    public static FirebaseAuthService Auth { get; } = new(Session);
    public static FirebaseDbService Db { get; } = new(Session);
}
