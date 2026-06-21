namespace PowerPlatform.ProductivityEngine.Core.Connections
{
    public class ConnectionProfile
    {
        public string EnvironmentUrl { get; set; } // e.g., "https://mydev.crm.dynamics.com"
        public string TenantId { get; set; } = "organizations";
        public string ClientId { get; set; } = "51f81489-12ee-4a9e-aaae-a2591f45987d";
        public string ClientSecret { get; set; } // Null if Certificate or Interactive
        public string ClientCertificateThumbprint { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool UseInteractiveAuth { get; set; } = true;
        public string RedirectUri { get; set; } = "http://localhost";
        public string ConnectionString { get; set; } // If provided, takes precedence over other fields
        public int TimeoutSeconds { get; set; } = 120;
    }
}
