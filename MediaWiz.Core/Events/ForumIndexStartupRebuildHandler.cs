using System.Threading;
using System.Threading.Tasks;
using Examine;
using Microsoft.Extensions.Logging;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;
using Umbraco.Cms.Infrastructure.Examine;

namespace MediaWiz.Forums.Events
{
    /// <summary>
    /// Ensures the ForumIndex is populated. After a content import/migration (or a
    /// fresh install) the index exists but is empty, and Umbraco does not rebuild
    /// custom indexes on startup — leaving forum search and active-topics blank.
    /// This rebuilds ForumIndex on startup when it holds no documents.
    /// </summary>
    public class ForumIndexStartupRebuildHandler : INotificationAsyncHandler<UmbracoApplicationStartedNotification>
    {
        private readonly IExamineManager _examineManager;
        private readonly IIndexRebuilder _indexRebuilder;
        private readonly ILogger<ForumIndexStartupRebuildHandler> _logger;

        public ForumIndexStartupRebuildHandler(IExamineManager examineManager, IIndexRebuilder indexRebuilder, ILogger<ForumIndexStartupRebuildHandler> logger)
        {
            _examineManager = examineManager;
            _indexRebuilder = indexRebuilder;
            _logger = logger;
        }

        public Task HandleAsync(UmbracoApplicationStartedNotification notification, CancellationToken cancellationToken)
        {
            if (!_examineManager.TryGetIndex("ForumIndex", out var index))
            {
                _logger.LogWarning("ForumIndexStartupRebuild: ForumIndex not found");
                return Task.CompletedTask;
            }

            long docCount = 0;
            try
            {
                docCount = index.Searcher.CreateQuery().All().Execute().TotalItemCount;
            }
            catch { /* new index with no searchable segments yet */ }

            if (docCount == 0 && _indexRebuilder.CanRebuild(index.Name))
            {
                _logger.LogInformation("ForumIndexStartupRebuild: ForumIndex is empty — rebuilding");
                _indexRebuilder.RebuildIndex(index.Name);
            }
            else
            {
                _logger.LogInformation("ForumIndexStartupRebuild: ForumIndex has {Count} documents — no rebuild needed", docCount);
            }

            return Task.CompletedTask;
        }
    }
}
