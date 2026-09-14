using MediaWiz.Forums.Services;
using Microsoft.Extensions.DependencyInjection;
using Umbraco.Cms.Core.Composing;
using Umbraco.Cms.Core.DependencyInjection;

namespace MediaWiz.Forums.Composers
{
    /// <summary>
    /// Registers Cloudflare Turnstile verification, used to gate the forum login
    /// and registration POSTs (replacing the native math captcha). Keys come from
    /// the site's existing "uCaptcha" config section.
    /// </summary>
    public class TurnstileComposer : IComposer
    {
        public void Compose(IUmbracoBuilder builder)
        {
            builder.Services.AddHttpClient();
            builder.Services.AddSingleton<ITurnstileVerifier, TurnstileVerifier>();
        }
    }
}
