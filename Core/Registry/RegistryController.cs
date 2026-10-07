using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using V12.Core.Core.Interfaces;

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

        /// <summary>
        /// Returns the service instance registered under the given name, cast to <typeparamref name="T"/>.
        /// Returns null if not found or the type does not match.
        /// </summary>
        public T? Get<T>(string serviceName) where T : class
        {
            return RegisteredServices.FirstOrDefault(x => x.name == serviceName)?.ServiceInstance as T;
        }

        /// <summary>
        /// Returns the first registered service whose instance is of type <typeparamref name="T"/>.
        /// </summary>
        public T? Get<T>() where T : class
        {
            return RegisteredServices.FirstOrDefault(x => x.ServiceInstance is T)?.ServiceInstance as T;
        }

        /// <summary>
        /// Returns all registered service instances that are of type <typeparamref name="T"/>.
        /// </summary>
        public IEnumerable<T> GetAll<T>() where T : class
        {
            return RegisteredServices
                .Where(x => x.ServiceInstance is T)
                .Select(x => (T)x.ServiceInstance);
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

        /// <summary>
        /// Convenience overload to register a named service instance directly.
        /// </summary>
        public bool Register(string name, object instance)
        {
            return Register(new RegistryService(name, instance));
        }

        /// <summary>
        /// Register a named service, replacing any existing service with the
        /// same name. Returns true when a previous registration was replaced,
        /// false when the name was free.
        /// </summary>
        public bool RegisterOrReplace(string name, object instance)
        {
            var existing = RegisteredServices.FirstOrDefault(x => x.name == name);
            if (existing != null)
            {
                existing.ServiceInstance = instance;
                return true;
            }
            RegisteredServices.Add(new RegistryService(name, instance));
            return false;
        }

        public bool Unregister(RegistryService service)
        {
            if (RegisteredServices.Where(x => x.name == service.name).FirstOrDefault() == null) return false;
            RegisteredServices.Remove(service);
            return true;
        }

        /// <summary>
        /// Unregister a service by name.
        /// </summary>
        public bool Unregister(string serviceName)
        {
            var service = RegisteredServices.FirstOrDefault(x => x.name == serviceName);
            if (service == null) return false;
            RegisteredServices.Remove(service);
            return true;
        }

        /// <summary>
        /// Calls <see cref="IGameService.Update"/> on every registered service that implements <see cref="IGameService"/>.
        /// </summary>
        public void Update(float deltaTime)
        {
            foreach (var service in GetAll<IGameService>().ToArray())
                service.Update(deltaTime);
        }
    }
}
