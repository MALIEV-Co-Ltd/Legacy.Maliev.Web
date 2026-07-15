// <copyright file="Index.cshtml.cs" company="Maliev Company Limited">
// Copyright (c) Maliev Company Limited. All rights reserved.
// </copyright>

namespace Legacy.Maliev.Web.Pages
{
    using Microsoft.AspNetCore.Mvc.RazorPages;

    /// <summary>
    /// About Model.
    /// </summary>
    /// <seealso cref="Microsoft.AspNetCore.Mvc.RazorPages.PageModel" />
    public class AboutModel : PageModel
    {
        /// <summary>
        /// Gets or sets message.
        /// </summary>
        /// <value>
        /// The message.
        /// </value>
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// GET.
        /// </summary>
        public void OnGet()
        {
            this.Message = "Your application description page.";
        }
    }
}
