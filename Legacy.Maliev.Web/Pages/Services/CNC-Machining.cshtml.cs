// <copyright file="CNC-Machining.cshtml.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Pages.Services
{
    using Microsoft.AspNetCore.Mvc;
    using Microsoft.AspNetCore.Mvc.RazorPages;

    /// <summary>
    /// Machining.
    /// </summary>
    /// <seealso cref="Microsoft.AspNetCore.Mvc.RazorPages.PageModel" />
    public class Machining : PageModel
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Machining" /> class.
        /// </summary>
        public Machining()
        {
        }

        /// <summary>
        /// OnGet.
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
