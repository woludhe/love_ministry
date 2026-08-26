using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ChurchMinistry.Models
{
    /// <summary>
    /// Configurable payment method shown on the giving page.
    /// Admin sets this up once; giving page reads it dynamically.
    /// </summary>
    public class PaymentMethodConfig
    {
        public int Id { get; set; }

        // ── Identity ──────────────────────────────────────────
        [Required, MaxLength(80)]
        public string Name { get; set; } = "";          // "MTN Mobile Money"

        [Required, MaxLength(40)]
        public string Code { get; set; } = "";          // "mtn", "airtel", "mpesa", "visa", "cash"

        [MaxLength(10)]
        public string BadgeLabel { get; set; } = "";    // "MTN", "AIR", "M-P", "VISA"

        [MaxLength(7)]
        public string BadgeColor { get; set; } = "#6366f1"; // hex color for the badge

        // ── Currency ──────────────────────────────────────────
        [Required, MaxLength(10)]
        public string Currency { get; set; } = "UGX";   // ISO code

        [MaxLength(5)]
        public string CurrencySymbol { get; set; } = "UGX"; // display symbol

        // ── Integration type ──────────────────────────────────
        /// <summary>Flutterwave = use Flutterwave checkout; Manual = show custom instructions</summary>
        public PaymentIntegrationType IntegrationType { get; set; }
            = PaymentIntegrationType.Manual;

        // ── Manual instructions (shown when IntegrationType = Manual) ──
        [MaxLength(2000)]
        public string? InstructionsText { get; set; }   // rich text instructions

        [MaxLength(500)]
        public string? InstructionsImagePath { get; set; } // uploaded QR/paybill image

        // ── Default amounts ───────────────────────────────────
        /// <summary>Comma-separated amounts e.g. "5000,10000,20000,50000"</summary>
        [MaxLength(200)]
        public string? DefaultAmounts { get; set; } = "5000,10000,20000,50000";

        // ── Metadata ──────────────────────────────────────────
        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // ── Helper: parse default amounts ─────────────────────
        [NotMapped]
        public List<decimal> ParsedAmounts =>
            (DefaultAmounts ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(a => decimal.TryParse(a.Trim(), out var v) ? v : 0)
            .Where(v => v > 0)
            .ToList();
    }

    public enum PaymentIntegrationType
    {
        /// <summary>Process via Flutterwave checkout</summary>
        Flutterwave = 0,

        /// <summary>Show manual instructions (text + optional image)</summary>
        Manual = 1
    }
}
