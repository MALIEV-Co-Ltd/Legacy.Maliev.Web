// <copyright file="3D-Scanning.cshtml.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Pages.Services
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.RazorPages;

    /// <summary>
    /// Scanning model.
    /// </summary>
    /// <seealso cref="Microsoft.AspNetCore.Mvc.RazorPages.PageModel" />
    public class ScanningModel : PageModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ScanningModel" /> class.
        /// </summary>
        public ScanningModel()
        {
        }

        /// <summary>
        /// GET.
        /// </summary>
        /// <returns>
        ///   <see cref="IActionResult" />.
        /// </returns>
        public IActionResult OnGet()
        {
            return this.Page();
        }
    }
}
