using Microsoft.AspNetCore.Authentication.Twitter;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

namespace AEMInterfaceService.Pages.Models
{
    public class AEMTransactionRegistration
    {
        List<AEMTransaction> aemTransactionList;
        static AEMTransactionRegistration casregd = null;

        private AEMTransactionRegistration()
        {
            aemTransactionList = new List<AEMTransaction>();
        }

        public static AEMTransactionRegistration getInstance()
        {
            if (casregd == null)
            {
                casregd = new AEMTransactionRegistration();
                return casregd;
            }
            else
            {
                return casregd;
            }
        }

        public void Add(AEMTransaction aemTransaction)
        {
            aemTransactionList.Add(aemTransaction);
        }

        public List<AEMTransaction> getAllAEMTransaction()
        {
            return aemTransactionList;
        }

        public List<AEMTransaction> getUpdateAEMTransaction()
        {
            return aemTransactionList;
        }
    }
}
