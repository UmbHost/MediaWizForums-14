using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MediaWiz.Forums.Services
{
    /// <summary>
    /// Server-side Cloudflare Turnstile verification (siteverify). Reads the keys
    /// from the site's existing uCaptcha config section:
    ///   "uCaptcha": { "SiteKey": "...", "SecretKey": "...", "Provider": "Turnstile" }
    /// Used to gate the forum login + registration POSTs, replacing the native
    /// math captcha.
    /// </summary>
    public interface ITurnstileVerifier
    {
        /// <summary>True when a Turnstile secret is configured (Provider == Turnstile).</summary>
        bool IsEnabled { get; }

        /// <summary>The public site key (for the widget), or null when not configured.</summary>
        string SiteKey { get; }

        /// <summary>
        /// Validates a Turnstile token via siteverify. Returns true only on
        /// success === true and (when supplied) a matching action. Fails closed on
        /// any error. Returns true when Turnstile is not configured (nothing to gate).
        /// </summary>
        Task<bool> VerifyAsync(string token, string remoteIp, string expectedAction);
    }

    public class TurnstileVerifier : ITurnstileVerifier
    {
        private const string VerifyUrl = "https://challenges.cloudflare.com/turnstile/v0/siteverify";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _config;
        private readonly ILogger<TurnstileVerifier> _logger;

        public TurnstileVerifier(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<TurnstileVerifier> logger)
        {
            _httpClientFactory = httpClientFactory;
            _config = config;
            _logger = logger;
        }

        private string Secret => _config["uCaptcha:SecretKey"];
        public string SiteKey => _config["uCaptcha:SiteKey"];

        public bool IsEnabled =>
            !string.IsNullOrWhiteSpace(Secret) &&
            string.Equals(_config["uCaptcha:Provider"], "Turnstile", StringComparison.OrdinalIgnoreCase);

        public async Task<bool> VerifyAsync(string token, string remoteIp, string expectedAction)
        {
            if (!IsEnabled)
            {
                return true; // nothing to gate on
            }
            if (string.IsNullOrWhiteSpace(token) || token.Length > 2048)
            {
                return false;
            }

            try
            {
                var fields = new Dictionary<string, string>
                {
                    ["secret"] = Secret,
                    ["response"] = token
                };
                if (!string.IsNullOrWhiteSpace(remoteIp))
                {
                    fields["remoteip"] = remoteIp;
                }

                var client = _httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(10);

                using var content = new FormUrlEncodedContent(fields);
                using var response = await client.PostAsync(VerifyUrl, content);
                if (!response.IsSuccessStatusCode)
                {
                    return false;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var success = root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.True;
                if (!success)
                {
                    return false;
                }

                // Bind the token to the surface it was issued for.
                if (!string.IsNullOrWhiteSpace(expectedAction) &&
                    root.TryGetProperty("action", out var a) && a.ValueKind == JsonValueKind.String)
                {
                    var action = a.GetString();
                    if (!string.IsNullOrEmpty(action) &&
                        !string.Equals(action, expectedAction, StringComparison.Ordinal))
                    {
                        _logger.LogWarning("Turnstile action mismatch: expected {Expected}, got {Actual}", expectedAction, action);
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Turnstile siteverify failed");
                return false; // fail closed
            }
        }
    }
}
