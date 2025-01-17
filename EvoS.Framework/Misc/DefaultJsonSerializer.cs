using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.IO;

namespace EvoS.Framework.Misc
{
    public static class DefaultJsonSerializer
    {
        private static JsonSerializer s_serializer;

        public static JsonSerializer Get()
        {
            if (s_serializer == null)
            {
                s_serializer = new JsonSerializer();
                s_serializer.NullValueHandling = NullValueHandling.Ignore;
                s_serializer.Converters.Add(new StringEnumConverter());
            }
            return s_serializer;
        }

        public static string Serialize(object o)
        {
            StringWriter stringWriter = new StringWriter();
            Get().Serialize(stringWriter, o);
            return stringWriter.ToString();
        }

        public static T Deserialize<T>(string json)
        {
            JsonTextReader reader = new JsonTextReader(new StringReader(json));
            return Get().Deserialize<T>(reader);
        }
    }

}