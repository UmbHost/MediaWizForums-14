using Examine;
using Lucene.Net.Documents;
using MediaWiz.Forums.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Models;
using Umbraco.Cms.Core.Models.PublishedContent;
using Umbraco.Cms.Core.Web;
using Umbraco.Cms.Infrastructure.Examine;
using Umbraco.Extensions;

namespace MediaWiz.Forums.Indexing
{
    public class ForumIndexValueSetBuilder : IValueSetBuilder<IContent>
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IUmbracoContextFactory _context;
        private readonly ILogger<ForumIndexValueSetBuilder> _logger;
        public ForumIndexValueSetBuilder(IServiceScopeFactory scopeFactory,IUmbracoContextFactory context, ILogger<ForumIndexValueSetBuilder> logger)
        {
            _scopeFactory = scopeFactory;
            _context = context;
            _logger = logger;
        }
        public IEnumerable<ValueSet> GetValueSets(params IContent[] contents)
        {
            _context.EnsureUmbracoContext();
            using var scope = _scopeFactory.CreateScope();

            var _cacheService = scope.ServiceProvider.GetRequiredService<IForumCacheService>();
            var _publishedContent = scope.ServiceProvider.GetRequiredService<IPublishedContentQuery>();

            var results = new List<ValueSet>();
            int candidates = 0, skippedNoPublished = 0, errored = 0;

            foreach (var content in contents)
            {
                if (content.ContentType.Alias != "forum" && content.ContentType.Alias != "forumPost")
                {
                    continue;
                }
                candidates++;

                // Build each item defensively: a single throw here (e.g. an
                // unresolved published node) must NOT abort indexing of every
                // other forum post in the batch.
                try
                {
                    var forumid = 0;
                    if (content.ContentType.Alias == "forumPost")
                    {
                        if (content.GetValue<int>("postType") == 1)
                        {
                            forumid = content.ParentId;
                        }
                        else
                        {
                            var parentContent = _publishedContent.Content(content.ParentId);
                            forumid = parentContent?.Parent()?.Id ?? 0;
                        }
                    }
                    var post = _publishedContent.Content(content.Id);
                    if (post == null)
                    {
                        // Not (yet) in the published cache — can't index it fully.
                        skippedNoPublished++;
                        continue;
                    }
                    var postParent = post.Parent<IPublishedContent>();
                    var cacheInfo = _cacheService.GetPost(post, "Topic_" + content.Id, new TimeSpan(0, 0, 10));

                    var htmlText = MessageParser.StripMarkup(content.GetValue<string>("postBody"));
                    var plainText = MessageParser.ExtractMerged(htmlText);
                    var forumDescription = MessageParser.ExtractMerged(content.GetValue<string>("forumDescription"));

                    var indexValues = new Dictionary<string, object>
                    {
                        ["__Key"] = content.Key,
                        // ContentValueSetValidator requires a "path" to run its
                        // recycle-bin / protected-content checks; without it every
                        // value set fails validation silently and nothing is indexed.
                        ["path"] = content.Path,
                        ["nodeType"] = content.ContentType.Id,
                        ["nodeName"] = content.GetValue<int>("postType") == 0 && content.ContentType.Alias != "forum" ? (postParent?.Name + ":" + content.Name) : content.Name,
                        ["message"] = string.IsNullOrEmpty(forumDescription) ? plainText : forumDescription,
                        ["raw_message"] = string.IsNullOrEmpty(forumDescription) ? htmlText : forumDescription,
                        ["author"] = content.GetValue<string>("postCreator"),
                        ["subject"] = content.GetValue<string>("forumTitle") ?? content?.GetValue<string>("postTitle") ?? postParent?.Value<string>("postTitle"),
                        ["edited"] = content.GetValue<DateTime?>("editDate"),
                        ["posttype"] = content.GetValue<int>("postType") == 1 ? "topic" : content.ContentType.Alias == "forum" ? "forum" : "reply",
                        ["updated"] = content.UpdateDate.Ticks,
                        ["replies"] = content.GetValue<int>("replyCount"),
                        ["answered"] = content.GetValue<bool>("answer") ? 1 : 0,
                        ["lastpost"] = cacheInfo.latestPost == DateTime.MinValue ? content.CreateDate : cacheInfo.latestPost,
                        ["lastTicks"] = cacheInfo.latestPost == DateTime.MinValue ? content.CreateDate.Ticks : cacheInfo.latestPost.Ticks,
                        ["forumid"] = forumid,
                        ["url"] = post.Url(),
                        ["status"] = content.GetValue<bool>("allowReplies") ? 1 : 0,
                        ["isActive"] = content.GetValue<bool>("isActive") ? 1 : 0,
                        ["postAtRoot"] = content.GetValue<bool>("postAtRoot") ? 1 : 0
                    };

                    results.Add(new ValueSet(content.Id.ToString(), IndexTypes.Content, content.ContentType.Alias, indexValues));
                }
                catch (Exception ex)
                {
                    errored++;
                    _logger.LogError(ex, "ForumIndexValueSetBuilder failed to build value set for node {Id} ({Alias})", content.Id, content.ContentType.Alias);
                }
            }

            if (candidates > 0)
            {
                _logger.LogDebug("ForumIndexValueSetBuilder: {Candidates} forum/forumPost, {Indexed} built, {Skipped} skipped, {Errored} errored",
                    candidates, results.Count, skippedNoPublished, errored);
            }

            return results;
        }
    }
}
