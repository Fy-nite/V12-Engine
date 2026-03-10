using System;
using System.Collections.Generic;
using System.Text;

namespace V12.Core.Registry
{
    public class RegistryController
    {
        public string Name { get; set; }
        public List<RegistryService> RegisteredServices;
        public RegistryController() { RegisteredServices = new List<RegistryService>(); }
        public RegistryController(string name) { Name = name; RegisteredServices = new List<RegistryService>(); }


        public RegistryService Get(string ServiceName)
        {
            return RegisteredServices.Where(x => x.name == ServiceName).FirstOrDefault();
        }
        public List<RegistryService> GetServices()
        {
            return RegisteredServices;
        }

        public bool Register(RegistryService service)
        {
            if (RegisteredServices.Where(x => x.name == service.name).FirstOrDefault() != null) return false;
            RegisteredServices.Add(service);
            return true;
        }

    }
}
