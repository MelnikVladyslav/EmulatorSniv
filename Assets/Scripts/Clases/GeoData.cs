using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Assets.Scripts.Clases
{
    [System.Serializable]
    public class GeoJsonData
    {
        public string type;
        public Feature[] features;
    }

    [System.Serializable]
    public class Feature
    {
        public string type;
        public Geometry geometry;
        [JsonProperty("properties")]
        public Dictionary<string, object> properties;
    }

    [System.Serializable]
    public class Geometry
    {
        public string type;

        [JsonProperty("coordinates")]
        public JToken coordinates;
    }

}
