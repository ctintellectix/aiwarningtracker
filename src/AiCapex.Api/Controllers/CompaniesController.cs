using AiCapex.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace AiCapex.Api.Controllers;

[ApiController]
[Route("api/companies")]
public sealed class CompaniesController(IAiCapexReadService readService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetCompanies(CancellationToken cancellationToken) =>
        Ok(await readService.GetCompaniesAsync(cancellationToken));

    [HttpGet("{ticker}")]
    public async Task<IActionResult> GetCompany(string ticker, CancellationToken cancellationToken)
    {
        var company = await readService.GetCompanyAsync(ticker, cancellationToken);
        return company is null ? NotFound() : Ok(company);
    }

    [HttpGet("{ticker}/metrics")]
    public async Task<IActionResult> GetMetrics(string ticker, CancellationToken cancellationToken) =>
        Ok(await readService.GetCompanyMetricsAsync(ticker, cancellationToken));
}
