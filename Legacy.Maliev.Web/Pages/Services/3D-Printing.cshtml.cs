// <copyright file="3D-Printing.cshtml.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Pages.Services
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.RazorPages;

    /// <summary>
    /// Additive model.
    /// </summary>
    /// <seealso cref="Microsoft.AspNetCore.Mvc.RazorPages.PageModel" />
    public class AdditiveModel : PageModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AdditiveModel" /> class.
        /// </summary>
        public AdditiveModel()
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
