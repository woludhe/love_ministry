using ChurchMinistry.Data;
using ChurchMinistry.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ChurchMinistry.Controllers
{
    [Authorize(Roles = "Admin,Pastor")]
    [Route("Admin/Payments")]
    public class AdminPaymentController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IWebHostEnvironment _env;

        public AdminPaymentController(ApplicationDbContext db, IWebHostEnvironment env)
        {
            _db = db; _env = env;
        }

        // ── Index ────────────────────────────────────────────
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            var methods = await _db.PaymentMethods
                .OrderBy(m => m.SortOrder)
                .ToListAsync();
            return View(methods);
        }

        // ── Create ───────────────────────────────────────────
        [HttpGet("Create")]
        public IActionResult Create() => View(new PaymentMethodConfig());

        [HttpPost("Create")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(PaymentMethodConfig model, IFormFile? instructionImage)
        {
            if (!ModelState.IsValid) return View(model);

            model.Code      = model.Code.ToLower().Trim().Replace(" ", "-");
            model.CreatedAt = DateTime.UtcNow;
            model.SortOrder = await _db.PaymentMethods.CountAsync() + 1;

            if (instructionImage != null)
                model.InstructionsImagePath = await SaveImageAsync(instructionImage);

            _db.PaymentMethods.Add(model);
            await _db.SaveChangesAsync();

            TempData["Success"] = $"Payment method \"{model.Name}\" created.";
            return RedirectToAction(nameof(Index));
        }

        // ── Edit ─────────────────────────────────────────────
        [HttpGet("Edit/{id}")]
        public async Task<IActionResult> Edit(int id)
        {
            var method = await _db.PaymentMethods.FindAsync(id);
            if (method == null) return NotFound();
            return View(method);
        }

        [HttpPost("Edit/{id}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, PaymentMethodConfig model,
            IFormFile? instructionImage, bool removeImage = false)
        {
            if (!ModelState.IsValid) return View(model);

            var existing = await _db.PaymentMethods.FindAsync(id);
            if (existing == null) return NotFound();

            existing.Name               = model.Name;
            existing.Code               = model.Code.ToLower().Trim().Replace(" ", "-");
            existing.BadgeLabel         = model.BadgeLabel;
            existing.BadgeColor         = model.BadgeColor;
            existing.Currency           = model.Currency.ToUpper().Trim();
            existing.CurrencySymbol     = model.CurrencySymbol;
            existing.IntegrationType    = model.IntegrationType;
            existing.InstructionsText   = model.InstructionsText;
            existing.DefaultAmounts     = model.DefaultAmounts;
            existing.IsActive           = model.IsActive;
            existing.UpdatedAt          = DateTime.UtcNow;

            if (removeImage)
            {
                DeleteImageFile(existing.InstructionsImagePath);
                existing.InstructionsImagePath = null;
            }
            else if (instructionImage != null)
            {
                DeleteImageFile(existing.InstructionsImagePath);
                existing.InstructionsImagePath = await SaveImageAsync(instructionImage);
            }

            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment method \"{existing.Name}\" updated.";
            return RedirectToAction(nameof(Index));
        }

        // ── Toggle active ─────────────────────────────────────
        [HttpPost("Toggle/{id}")]
        public async Task<IActionResult> Toggle(int id)
        {
            var method = await _db.PaymentMethods.FindAsync(id);
            if (method == null) return NotFound();
            method.IsActive    = !method.IsActive;
            method.UpdatedAt   = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            return Json(new { success = true, isActive = method.IsActive });
        }

        // ── Reorder ───────────────────────────────────────────
        [HttpPost("Reorder")]
        public async Task<IActionResult> Reorder([FromBody] List<int> orderedIds)
        {
            for (int i = 0; i < orderedIds.Count; i++)
            {
                var m = await _db.PaymentMethods.FindAsync(orderedIds[i]);
                if (m != null) { m.SortOrder = i + 1; m.UpdatedAt = DateTime.UtcNow; }
            }
            await _db.SaveChangesAsync();
            return Json(new { success = true });
        }

        // ── Delete ────────────────────────────────────────────
        [HttpPost("Delete/{id}")]
        public async Task<IActionResult> Delete(int id)
        {
            var method = await _db.PaymentMethods.FindAsync(id);
            if (method == null) return NotFound();
            DeleteImageFile(method.InstructionsImagePath);
            _db.PaymentMethods.Remove(method);
            await _db.SaveChangesAsync();
            TempData["Success"] = $"Payment method deleted.";
            return RedirectToAction(nameof(Index));
        }

        // ── Helpers ───────────────────────────────────────────
        private async Task<string> SaveImageAsync(IFormFile file)
        {
            var folder = Path.Combine(_env.WebRootPath, "uploads", "payment-methods");
            Directory.CreateDirectory(folder);
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var path     = Path.Combine(folder, fileName);
            await using var stream = new FileStream(path, FileMode.Create);
            await file.CopyToAsync(stream);
            return $"/uploads/payment-methods/{fileName}";
        }

        private void DeleteImageFile(string? relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return;
            var full = Path.Combine(_env.WebRootPath, relativePath.TrimStart('/'));
            if (System.IO.File.Exists(full)) System.IO.File.Delete(full);
        }
    }
}
