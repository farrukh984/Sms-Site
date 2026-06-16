using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Site.Models;
using System.Net.Http;
using System.Threading.Tasks;
using System.IO;

namespace Site.Controllers
{
    public class HomeController : Controller
    {
        private readonly IConfiguration _configuration;

        public HomeController(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public IActionResult Index()
        {
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                return RedirectToAction("Index", "Chat");
            }
            return RedirectToAction("Login", "Auth");
        }

        public IActionResult Privacy()
        {
            return View();
        }

        public IActionResult Services()
        {
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }

        [HttpPost]
        [Route("Home/AiChatProxy")]
        public async Task<IActionResult> AiChatProxy()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                var requestBody = await reader.ReadToEndAsync();
                
                // Parse the Gemini-format request from frontend
                var geminiRequest = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(requestBody);
                
                // Convert Gemini format to OpenAI/Groq format
                var messages = new System.Collections.Generic.List<object>();
                
                // Add system prompt if present
                if (geminiRequest.TryGetProperty("system_instruction", out var sysInstr))
                {
                    var sysText = sysInstr.GetProperty("parts")[0].GetProperty("text").GetString();
                    messages.Add(new { role = "system", content = sysText });
                }
                
                // Add conversation messages
                if (geminiRequest.TryGetProperty("contents", out var contents))
                {
                    foreach (var msg in contents.EnumerateArray())
                    {
                        var role = msg.GetProperty("role").GetString() == "user" ? "user" : "assistant";
                        var text = msg.GetProperty("parts")[0].GetProperty("text").GetString();
                        messages.Add(new { role, content = text });
                    }
                }
                
                // Build Groq request (OpenAI-compatible format)
                var groqPayload = new
                {
                    model = "llama-3.3-70b-versatile",
                    messages,
                    temperature = 0.7,
                    max_tokens = 600
                };
                
                using var httpClient = new HttpClient();
                var apiKey = _configuration["GeminiSettings:ApiKey"]; // We'll reuse this config key for Groq
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                
                var jsonPayload = System.Text.Json.JsonSerializer.Serialize(groqPayload);
                var content = new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync("https://api.groq.com/openai/v1/chat/completions", content);
                
                var responseBody = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode)
                {
                    // Return error in Gemini format so frontend handles it
                    var errorJson = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseBody);
                    var errMsg = "API error";
                    if (errorJson.TryGetProperty("error", out var errObj) && errObj.TryGetProperty("message", out var errMsgProp))
                        errMsg = errMsgProp.GetString() ?? "API error";
                    
                    var geminiError = new { error = new { code = (int)response.StatusCode, message = errMsg, status = "ERROR" } };
                    return StatusCode((int)response.StatusCode, System.Text.Json.JsonSerializer.Serialize(geminiError));
                }
                
                // Convert Groq/OpenAI response to Gemini format so frontend can parse it
                var groqResponse = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>(responseBody);
                var replyText = groqResponse.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                
                var geminiResponse = new
                {
                    candidates = new[]
                    {
                        new
                        {
                            content = new
                            {
                                parts = new[] { new { text = replyText } },
                                role = "model"
                            }
                        }
                    }
                };
                
                return Content(System.Text.Json.JsonSerializer.Serialize(geminiResponse), "application/json");
            }
            catch (System.Exception ex)
            {
                return StatusCode(500, System.Text.Json.JsonSerializer.Serialize(new { error = new { message = ex.Message } }));
            }
        }
    }
}
