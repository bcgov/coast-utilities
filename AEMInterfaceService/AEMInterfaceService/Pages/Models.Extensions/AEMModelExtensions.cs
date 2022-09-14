using Gov.Cscp.VictimServices.Public.JsonObjects;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AEMInterfaceService.Pages.Models.Extensions
{
    public static class AEMModelExtensions
    {
        public static AEMDynamicsModel ToAEMDynamicsModel(this AEMTransaction model)
        {
            AEMDynamicsModel aemTransaction = new AEMDynamicsModel();

            aemTransaction.AEMApp = model.AEMApp;
            aemTransaction.AEMForm = model.AEMForm;
            aemTransaction.AEMXMLData = model.AEMXMLData;
            return aemTransaction;
        }
    }
}

