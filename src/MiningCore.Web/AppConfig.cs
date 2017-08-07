namespace MiningCore
{
    public class AppConfig
    {
        public class EmailSenderConfig
        {
            public string ServerAddress { get; set; }
            public int ServerPort { get; set; }
            public string Username { get; set; }
            public string Password { get; set; }
        }

		public EmailSenderConfig EmailSender { get; set; }

	    public string SiteName { get; set; }
	    public string SiteDescription { get; set; }
	    public string CookieWarning { get; set; }
	    public string SupportEmail { get; set; }
	    public string NoScriptWarning { get; set; }
	    public string NoCompatibleBrowserWarning { get; set; }
		public string GoogleAnalyticsUA { get; set; }
	    public string TwitterUrl { get; set; }
	    public string FacebookUrl { get; set; }
	}
}
