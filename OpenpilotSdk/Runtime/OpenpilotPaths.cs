namespace OpenpilotSdk.Runtime
{
    public static class OpenpilotPaths
    {
        private const string ApplicationName = "OpenpilotToolkit";

        public static string ApplicationDataDirectory => EnsureDirectory(GetApplicationDataDirectory());

        public static string TempDirectory => EnsureDirectory(Path.Combine(ApplicationDataDirectory, "tmp"));

        public static string DeviceCacheFile => Path.Combine(ApplicationDataDirectory, "discoveredDevices.json");

        public static string BundledDirectory => AppContext.BaseDirectory;

        public static string GetBundledAssetPath(string fileName) => Path.Combine(BundledDirectory, fileName);

        public static string CombineUnixPath(params string[] segments)
        {
            return string.Join(
                '/',
                segments
                    .Where(segment => !string.IsNullOrWhiteSpace(segment))
                    .Select((segment, index) => index == 0
                        ? segment.TrimEnd('/')
                        : segment.Trim('/')));
        }

        private static string GetApplicationDataDirectory()
        {
            var baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(baseDirectory))
            {
                baseDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            }

            return Path.Combine(baseDirectory, ApplicationName);
        }

        private static string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
    }
}
