using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace MiningCore.Notifications.Slack
{
    public class SlackNotification
    {
        [JsonProperty("text")]
        public string Body { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Channel { get; set; }

        /// <summary>
        /// Customize Icon
        /// </summary>
        [JsonProperty("icon_emoji", NullValueHandling = NullValueHandling.Ignore)]
        public string Emoji { get; set; }

        /// <summary>
        ///  Customize Name
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Username { get; set; }
    }
}
