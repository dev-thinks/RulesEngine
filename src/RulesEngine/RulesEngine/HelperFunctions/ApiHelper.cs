using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Dynamic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace RulesEngine.HelperFunctions
{
    internal class ApiHelper
    {
        internal static async Task<T> Get<T>(string apiUrl)
        {
            return await DoVerb<T>("GET", apiUrl, null);
        }

        internal static async Task<T> Post<T>(string apiUrl, object payload)
        {
            return await DoVerb<T>("POST", apiUrl, payload);
        }

        private static async Task<T> DoVerb<T>(string verb, string apiUrl, object payload)
        {
            dynamic result = default;

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Accept.Clear();
                client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                HttpResponseMessage response = null;

                switch (verb)
                {
                    case "GET":
                        response = await client.GetAsync(apiUrl);
                        break;
                    case "POST":
                        HttpContent content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8,
                            "application/json");
                        response = await client.PostAsync(apiUrl, content);
                        break;
                }

                if (response != null && response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();

                    var converter = new ExpandoObjectConverter();
                    result = JsonConvert.DeserializeObject<ExpandoObject>(json, converter);
                }
                else
                {

                }
            }

            return result;
        }
    }
}