using Microsoft.Extensions.Options;
using MsAds.Auditor.Web.Services;

namespace MsAds.Auditor.Web.Pages;

public class ChangesModel(IReportStore store, IOptions<AuditorOptions> options)
    : ReportPageModel(store, options)
{
    public void OnGet() => LoadReport();
}
