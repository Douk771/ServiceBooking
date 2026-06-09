using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceBooking.API.DTOs.Companies;
using ServiceBooking.Core.Entities;
using ServiceBooking.Core.Enums;
using ServiceBooking.Infrastructure.Data;

namespace ServiceBooking.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CompaniesController(AppDbContext db, UserManager<AppUser> userManager) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CompanyDto>>> GetAll()
    {
        var companies = await db.Companies
            .Where(c => c.IsActive)
            .Select(c => new CompanyDto(c.Id, c.Name, c.Slug, c.Description, c.LogoUrl, c.Address, c.Phone, c.Email, c.AllowSelfBooking))
            .ToListAsync();

        return Ok(companies);
    }

    [HttpGet("my")]
    [Authorize]
    public async Task<ActionResult<List<CompanyDto>>> GetMy()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var companies = await db.CompanyMembers
            .Where(cm => cm.UserId == userId && cm.Role == UserRole.CompanyOwner && cm.Company.IsActive)
            .Select(cm => new CompanyDto(cm.Company.Id, cm.Company.Name, cm.Company.Slug, cm.Company.Description,
                cm.Company.LogoUrl, cm.Company.Address, cm.Company.Phone, cm.Company.Email, cm.Company.AllowSelfBooking))
            .ToListAsync();

        return Ok(companies);
    }

    [HttpGet("{slug}")]
    public async Task<ActionResult<CompanyDto>> GetBySlug(string slug)
    {
        var c = await db.Companies.FirstOrDefaultAsync(c => c.Slug == slug && c.IsActive);
        if (c is null) return NotFound();

        return Ok(new CompanyDto(c.Id, c.Name, c.Slug, c.Description, c.LogoUrl, c.Address, c.Phone, c.Email, c.AllowSelfBooking));
    }

    [HttpGet("{id:guid}/members")]
    [Authorize]
    public async Task<ActionResult<List<MemberDto>>> GetMembers(Guid id)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var members = await db.CompanyMembers
            .Include(cm => cm.User)
            .Where(cm => cm.CompanyId == id)
            .Select(cm => new MemberDto(cm.Id, cm.UserId, cm.User.FirstName, cm.User.LastName,
                cm.User.Email!, cm.User.AvatarUrl, cm.Role.ToString(), cm.Bio))
            .ToListAsync();

        return Ok(members);
    }

    [HttpPost]
    [Authorize]
    public async Task<ActionResult<CompanyDto>> Create(CreateCompanyDto dto)
    {
        if (await db.Companies.AnyAsync(c => c.Slug == dto.Slug))
            return Conflict("Slug already taken");

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var company = new Company
        {
            Id = Guid.NewGuid(),
            Name = dto.Name,
            Slug = dto.Slug,
            Description = dto.Description,
            Address = dto.Address,
            Phone = dto.Phone,
            Email = dto.Email,
            AllowSelfBooking = dto.AllowSelfBooking
        };

        var member = new CompanyMember
        {
            Id = Guid.NewGuid(),
            CompanyId = company.Id,
            UserId = userId,
            Role = UserRole.CompanyOwner
        };

        db.Companies.Add(company);
        db.CompanyMembers.Add(member);

        // Ensure user has CompanyOwner role
        var user = await userManager.FindByIdAsync(userId);
        if (user != null && !await userManager.IsInRoleAsync(user, "CompanyOwner"))
            await userManager.AddToRoleAsync(user, "CompanyOwner");

        await db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetBySlug), new { slug = company.Slug },
            new CompanyDto(company.Id, company.Name, company.Slug, company.Description, company.LogoUrl,
                company.Address, company.Phone, company.Email, company.AllowSelfBooking));
    }

    [HttpPut("{id:guid}")]
    [Authorize]
    public async Task<ActionResult<CompanyDto>> Update(Guid id, UpdateCompanyDto dto)
    {
        var company = await db.Companies.FindAsync(id);
        if (company is null) return NotFound();
        if (!await CanManageCompany(id)) return Forbid();

        if (dto.Name is not null) company.Name = dto.Name;
        if (dto.Description is not null) company.Description = dto.Description;
        if (dto.Address is not null) company.Address = dto.Address;
        if (dto.Phone is not null) company.Phone = dto.Phone;
        if (dto.Email is not null) company.Email = dto.Email;
        if (dto.AllowSelfBooking is not null) company.AllowSelfBooking = dto.AllowSelfBooking.Value;

        await db.SaveChangesAsync();

        return Ok(new CompanyDto(company.Id, company.Name, company.Slug, company.Description,
            company.LogoUrl, company.Address, company.Phone, company.Email, company.AllowSelfBooking));
    }

    [HttpPost("{id:guid}/members")]
    [Authorize]
    public async Task<ActionResult<MemberDto>> AddMember(Guid id, AddMemberDto dto)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var user = await userManager.FindByEmailAsync(dto.Email);
        if (user is null) return NotFound("User not found");

        var exists = await db.CompanyMembers.AnyAsync(cm => cm.CompanyId == id && cm.UserId == user.Id);
        if (exists) return Conflict("User is already a member");

        var role = Enum.Parse<UserRole>(dto.Role);
        var member = new CompanyMember
        {
            Id = Guid.NewGuid(),
            CompanyId = id,
            UserId = user.Id,
            Role = role,
            Bio = dto.Bio
        };

        db.CompanyMembers.Add(member);

        if (!await userManager.IsInRoleAsync(user, dto.Role))
            await userManager.AddToRoleAsync(user, dto.Role);

        await db.SaveChangesAsync();

        return Ok(new MemberDto(member.Id, user.Id, user.FirstName, user.LastName,
            user.Email!, user.AvatarUrl, dto.Role, dto.Bio));
    }

    [HttpDelete("{id:guid}/members/{memberId:guid}")]
    [Authorize]
    public async Task<IActionResult> RemoveMember(Guid id, Guid memberId)
    {
        if (!await CanManageCompany(id)) return Forbid();

        var member = await db.CompanyMembers.FirstOrDefaultAsync(cm => cm.Id == memberId && cm.CompanyId == id);
        if (member is null) return NotFound();

        db.CompanyMembers.Remove(member);
        await db.SaveChangesAsync();
        return NoContent();
    }

    private async Task<bool> CanManageCompany(Guid companyId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is null) return false;
        if (User.IsInRole("SuperAdmin")) return true;

        return await db.CompanyMembers.AnyAsync(cm =>
            cm.CompanyId == companyId &&
            cm.UserId == userId &&
            cm.Role == UserRole.CompanyOwner);
    }
}
