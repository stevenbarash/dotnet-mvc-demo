using Microsoft.AspNetCore.Mvc;

namespace DescopeWorkshop.Web.Controllers;

public class ProfileController : Controller
{
    public IActionResult Index()
    {
        ViewBag.Name = "Jane Doe";
        ViewBag.Email = "jane.doe@example.com";
        return View();
    }
}
