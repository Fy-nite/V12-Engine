using System;
using System.Collections.Generic;
using System.Text;

namespace V12.Core.Registry
{
    /// <summary>
    /// The registry service is a class that represents a service that can be registered in the registry controller. It contains a name and an instance of the service. The name is used to identify the service in the registry controller, and the instance is the actual service that can be used by other parts of the application.
    /// </summary>
    public class RegistryService
    {
        public RegistryService() { }
        public string name;
        public object ServiceInstance;
        public RegistryService(string name, object serviceInstance)
        {
            this.name = name;
            this.ServiceInstance = serviceInstance;
        }
    }
}
