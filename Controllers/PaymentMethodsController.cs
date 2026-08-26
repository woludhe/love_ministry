using ChurchMinistry.Data;
using ChurchMinistry.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    /// <summary>
    /// Public API used by Live.cshtml to load active payment methods dynamically.
    /// No auth required — the giving sidebar is visible to guests too.
    /// </summary>
    [Route("api/PaymentMethods")]
    [ApiController]
    public class PaymentMethodsApiController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        public PaymentMethodsApiController(ApplicationDbContext db) => _db = db;

        /// <summary>
        /// GET /api/PaymentMethods/Active
        /// Returns all active payment methods ordered by SortOrder.
        /// Used by Live.cshtml giving sidebar.
        /// </summary>
        [HttpGet("Active")]
        public async Task<IActionResult> Active()
        {
            var methods = await _db.PaymentMethods
                .Where(m => m.IsActive)
                .OrderBy(m => m.SortOrder)
                .Select(m => new
                {
                    code             = m.Code,
                    name             = m.Name,
                    badgeLabel       = m.BadgeLabel,
                    badgeColor       = m.BadgeColor,
                    currency         = m.Currency,
                    currencySymbol   = m.CurrencySymbol,
                    integrationType  = m.IntegrationType.ToString().ToLower(),
                    defaultAmounts   = m.ParsedAmounts,
                    instructionsText = m.InstructionsText,
                })
                .ToListAsync();

            return Ok(methods);
        }
    }
}
