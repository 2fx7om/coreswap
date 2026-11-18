namespace CoreSwap.Config;

public static class FirebaseConfig
{
    // Firebase Console > Project settings > General > Web API Key
    public const string ApiKey = "AIzaSyBzr5zOJIlHE1t0-04ipkZ1iNQsyGNW3zk";

    // Firebase Console > Realtime Database > Data
    // Copy the exact URL shown there, for example:
    // https://your-project-default-rtdb.asia-southeast1.firebasedatabase.app
    public const string DatabaseUrl = "https://coreswap-e0186-default-rtdb.asia-southeast1.firebasedatabase.app/";

    public static string CleanDatabaseUrl => DatabaseUrl.TrimEnd('/');
}
