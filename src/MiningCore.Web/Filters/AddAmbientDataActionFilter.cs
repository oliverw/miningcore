using System;
using System.Globalization;
using MiningCore.Extensions;
using MiningCore.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace MiningCore.Filters
{
    public class AddAmbientDataActionFilter : IActionFilter
    {
        public const string Key = "Ambient";

        public void OnActionExecuting(ActionExecutingContext context)
        {
            var model = new AmbientData();

            model.ServerDate = DateTime.UtcNow;
            model.ContentLanguage = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
            
            // set result
            var controller = context.Controller as Controller;
            if (controller?.ViewData != null)
                controller.ViewData[Key] = model;
        }

        public void OnActionExecuted(ActionExecutedContext context)
        {
        }
    }
}
