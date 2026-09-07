using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using VisionCart.Application.Admin;

namespace VisionCart.Web.Areas.Admin.ViewComponents;

/// <summary>
/// The bell in the back-office header.
///
/// There is no notifications table and this deliberately does not add one — a
/// second copy of "a prescription is waiting" would immediately start
/// disagreeing with the first. Instead the panel is a live read of the work
/// the shop already knows is outstanding: prescriptions waiting on an
/// optician, orders waiting on payment, and colourways below their restock
/// level.
///
/// It sits in the layout, so it renders on every back-office page. The result
/// is cached for half a minute to keep that from turning one page view into a
/// second dashboard's worth of queries; the figures are a queue depth, not a
/// balance, and thirty seconds of staleness cannot mislead anybody.
/// </summary>
public sealed class WorkQueueViewComponent(IDashboardService dashboard, IMemoryCache cache) : ViewComponent
{
    private const string CacheKey = "admin:work-queue";
    private static readonly TimeSpan Freshness = TimeSpan.FromSeconds(30);

    public async Task<IViewComponentResult> InvokeAsync()
    {
        var view = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = Freshness;
            return await dashboard.BuildAsync(HttpContext.RequestAborted);
        });

        return View(view ?? new DashboardView());
    }
}
