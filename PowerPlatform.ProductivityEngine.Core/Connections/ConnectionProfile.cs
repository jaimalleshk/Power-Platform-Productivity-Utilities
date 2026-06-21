namespace PowerPlatform.ProductivityEngine.Core.Connections
{
    public class ConnectionProfile
    {
        public string EnvironmentUrl { get; set; } // e.g., "https://mydev.crm.dynamics.com"
        public string TenantId { get; set; }
        public string ClientId { get; set; }
        public string ClientSecret { get; set; } // Null if Certificate or Interactive
        public string ClientCertificateThumbprint { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseInteractiveAuth { get; set; }
        public string ConnectionString { get; set; } // If provided, takes precedence over other fields
        public int TimeoutSeconds { get; set; } = 120;
    }
}
