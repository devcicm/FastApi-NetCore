namespace FastApi_NetCore
{
    public class ServerConfig
    {
        public string HttpPrefix { get; set; } = "http://localhost:8080/";
        public string JwtSecretKey { get; set; } = "";
        public string[] JwtExcludedPaths { get; set; } = new string[] { "/public", "/login" };
        public string[] IpWhitelist { get; set; } = new string [0]  ;
        public string[] IpBlacklist { get; set; } = new string[0];
        public string[] IpPool { get; set; } = new string [0];
        public bool IsProduction { get; set; } = false;
        public string DevelopmentAuthKeyword { get; set; } = "mode_dev";
        public bool EnableApiKeys { get; set; } = false;
        public bool EnableRateLimiting { get; set; } = false;
        public bool EnableDetailedLogging { get; set; } = false;
    }
}