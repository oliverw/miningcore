using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MiningCore.Filters;
using MiningCore.Models;
using Microsoft.AspNetCore.Mvc;

namespace MiningCore.Extensions
{
    public static class ControllerExtensions
    {
        public static AmbientData GetAmbientData(this Controller controller)
        {
            return (AmbientData) controller.ViewData[AddAmbientDataActionFilter.Key];
        }
    }
}
