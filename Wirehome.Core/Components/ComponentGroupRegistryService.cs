﻿using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Wirehome.Core.Components.Exceptions;
using Wirehome.Core.Contracts;
using Wirehome.Core.Diagnostics;
using Wirehome.Core.MessageBus;
using Wirehome.Core.Model;
using Wirehome.Core.Storage;

namespace Wirehome.Core.Components
{
    /// <summary>
    /// TODO: Create bus messages when something has changed (settings etc.)
    /// </summary>
    public class ComponentGroupRegistryService : IService
    {
        private const string ComponentGroupsDirectory = "ComponentGroups";

        private readonly Dictionary<string, ComponentGroup> _componentGroups = new Dictionary<string, ComponentGroup>();

        private readonly StorageService _storageService;
        private readonly MessageBusService _messageBusService;

        private readonly ILogger _logger;

        public ComponentGroupRegistryService(
            StorageService storageService,
            SystemStatusService systemInformationService,
            MessageBusService messageBusService,
            ILoggerFactory loggerFactory)
        {
            _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
            _messageBusService = messageBusService ?? throw new ArgumentNullException(nameof(messageBusService));

            if (loggerFactory == null) throw new ArgumentNullException(nameof(loggerFactory));
            _logger = loggerFactory.CreateLogger<ComponentGroupRegistryService>();

            if (systemInformationService == null) throw new ArgumentNullException(nameof(systemInformationService));
            systemInformationService.Set("component_group_registry.count", () => _componentGroups.Count);
        }

        public void Start()
        {
            foreach (var componentGroupUid in GetComponentGroupUids())
            {
                TryInitializeComponentGroup(componentGroupUid);
            }
        }

        public List<string> GetComponentGroupUids()
        {
            return _storageService.EnumeratureDirectories("*", ComponentGroupsDirectory);
        }

        public ComponentGroupConfiguration ReadComponentGroupConfiguration(string uid)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));

            if (!_storageService.TryRead(out ComponentGroupConfiguration configuration, ComponentGroupsDirectory, uid, DefaultFilenames.Configuration))
            {
                throw new ComponentGroupNotFoundException(uid);
            }

            return configuration;
        }

        public void WriteComponentGroupConfiguration(string uid, ComponentGroupConfiguration configuration)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            _storageService.Write(configuration, ComponentGroupsDirectory, DefaultFilenames.Configuration);
        }

        public void DeleteComponentGroup(string uid)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));

            _storageService.DeleteDirectory(ComponentGroupsDirectory, uid);
        }

        public void TryInitializeComponentGroup(string uid)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));

            try
            {
                if (!_storageService.TryRead(out ComponentGroupConfiguration configuration, ComponentGroupsDirectory, uid, DefaultFilenames.Configuration))
                {
                    throw new ComponentGroupNotFoundException(uid);
                }

                if (!_storageService.TryRead(out WirehomeDictionary settings, ComponentGroupsDirectory, uid, DefaultFilenames.Settings))
                {
                    settings = new WirehomeDictionary();
                }

                var componentGroup = new ComponentGroup(uid);
                foreach (var setting in settings)
                {
                    componentGroup.Settings[setting.Key] = setting.Value;
                }

                var associationUids = _storageService.EnumeratureDirectories("*", ComponentGroupsDirectory, uid, "Components");
                foreach (var associationUid in associationUids)
                {
                    if (!_storageService.TryRead(out WirehomeDictionary associationSettings, ComponentGroupsDirectory, uid, "Components", associationUid, DefaultFilenames.Settings))
                    {
                        associationSettings = new WirehomeDictionary();
                    }

                    var componentAssociation = new ComponentGroupAssociation();
                    foreach (var associationSetting in associationSettings)
                    {
                        componentAssociation.Settings[associationSetting.Key] = associationSetting.Value;
                    }

                    componentGroup.Components.TryAdd(associationUid, componentAssociation);
                }

                lock (_componentGroups)
                {
                    _componentGroups[uid] = componentGroup;
                }
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, $"Error while initializing component group '{uid}'.'");
            }
        }

        public List<ComponentGroup> GetComponentGroups()
        {
            lock (_componentGroups)
            {
                return new List<ComponentGroup>(_componentGroups.Values);
            }
        }

        public bool TryGetComponentGroup(string uid, out ComponentGroup componentGroup)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));

            lock (_componentGroups)
            {
                return _componentGroups.TryGetValue(uid, out componentGroup);
            }
        }

        public ComponentGroup GetComponentGroup(string uid)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));

            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(uid, out var componentGroup))
                {
                    throw new ComponentGroupNotFoundException(uid);
                }

                return componentGroup;
            }
        }

        public void AssignComponent(string componentGroupUid, string componentUid)
        {
            if (componentGroupUid == null) throw new ArgumentNullException(nameof(componentGroupUid));
            if (componentUid == null) throw new ArgumentNullException(nameof(componentUid));

            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(componentGroupUid, out var componentGroup))
                {
                    throw new ComponentGroupNotFoundException(componentUid);
                }

                if (componentGroup.Components.ContainsKey(componentUid))
                {
                    return;
                }

                componentGroup.Components[componentUid] = new ComponentGroupAssociation();

                Save();
            }
        }

        public void UnassignComponent(string componentGroupUid, string componentUid)
        {
            if (componentGroupUid == null) throw new ArgumentNullException(nameof(componentGroupUid));
            if (componentUid == null) throw new ArgumentNullException(nameof(componentUid));

            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(componentGroupUid, out var componentGroup))
                {
                    throw new ComponentGroupNotFoundException(componentUid);
                }

                if (!componentGroup.Components.Remove(componentUid, out _))
                {
                    return;
                }

                Save();
            }
        }

        public object GetComponentAssociationSetting(string componentGroupUid, string componentUid, string settingUid)
        {
            if (componentGroupUid == null) throw new ArgumentNullException(nameof(componentGroupUid));
            if (componentUid == null) throw new ArgumentNullException(nameof(componentUid));
            if (settingUid == null) throw new ArgumentNullException(nameof(settingUid));

            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(componentGroupUid, out var componentGroup))
                {
                    throw new ComponentGroupNotFoundException(componentUid);
                }

                if (!componentGroup.Components.TryGetValue(componentUid, out var association))
                {
                    return null;
                }

                if (!association.Settings.TryGetValue(settingUid, out var settingValue))
                {
                    return null;
                }

                return settingValue;
            }
        }

        public void SetComponentAssociationSetting(string componentGroupUid, string componentUid, string settingUid, object settingValue)
        {
            if (componentGroupUid == null) throw new ArgumentNullException(nameof(componentGroupUid));
            if (componentUid == null) throw new ArgumentNullException(nameof(componentUid));
            if (settingUid == null) throw new ArgumentNullException(nameof(settingUid));

            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(componentGroupUid, out var componentGroup))
                {
                    throw new ComponentGroupNotFoundException(componentUid);
                }

                if (!componentGroup.Components.TryGetValue(componentUid, out var association))
                {
                    return;
                }

                association.Settings[settingUid] = settingValue;

                Save();
            }
        }

        public void RemoveComponentAssociationSetting(string componentGroupUid, string componentUid, string settingUid)
        {
            if (componentGroupUid == null) throw new ArgumentNullException(nameof(componentGroupUid));
            if (componentUid == null) throw new ArgumentNullException(nameof(componentUid));
            if (settingUid == null) throw new ArgumentNullException(nameof(settingUid));

            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(componentGroupUid, out var componentGroup))
                {
                    throw new ComponentGroupNotFoundException(componentUid);
                }

                if (!componentGroup.Components.TryGetValue(componentUid, out var association))
                {
                    return;
                }

                if (!association.Settings.Remove(settingUid, out _))
                {
                    return;
                }

                Save();
            }
        }

        public object GetComponentGroupSetting(string uid, string settingUid)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));
            if (settingUid == null) throw new ArgumentNullException(nameof(settingUid));

            ComponentGroup componentGroup;
            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(uid, out componentGroup))
                {
                    return null;
                }
            }

            return componentGroup.Settings.GetValueOrDefault(settingUid);
        }

        public void SetComponentGroupSetting(string uid, string settingUid, object value)
        {
            if (uid == null) throw new ArgumentNullException(nameof(uid));
            if (settingUid == null) throw new ArgumentNullException(nameof(settingUid));

            ComponentGroup componentGroup;
            lock (_componentGroups)
            {
                if (!_componentGroups.TryGetValue(uid, out componentGroup))
                {
                    return;
                }
            }

            componentGroup.Settings.TryGetValue(settingUid, out var oldValue);
            if (Equals(oldValue, value))
            {
                return;
            }

            componentGroup.Settings[settingUid] = value;
            _storageService.Write(componentGroup.Settings, "ComponentGroups", uid);

            _logger.Log(
                LogLevel.Debug,
                "Component group '{0}' setting '{1}' changed from '{2}' to '{3}'.",
                uid,
                settingUid,
                oldValue ?? "<null>",
                value ?? "<null>");

            _messageBusService.Publish(new WirehomeDictionary()
                .WithType("component_group_registry.event.setting_changed")
                .WithValue("component_group_uid", uid)
                .WithValue("setting_uid", settingUid)
                .WithValue("old_value", oldValue)
                .WithValue("new_value", oldValue));
        }

        public object RemoveComponentGroupSetting(string componentGroupUid, string settingUid)
        {
            throw new NotImplementedException();
        }

        private void Save()
        {
            foreach (var componentGroup in _componentGroups.Values)
            {
                var configuration = new ComponentGroupConfiguration();
                _storageService.Write(configuration, "ComponentGroups", componentGroup.Uid, DefaultFilenames.Configuration);

                foreach (var componentAssociation in componentGroup.Components)
                {
                    _storageService.Write(componentAssociation.Value.Settings, "ComponentGroups", componentGroup.Uid, "Components", componentAssociation.Key, DefaultFilenames.Settings);
                }

                foreach (var componentAssociation in componentGroup.Macros)
                {
                    _storageService.Write(componentAssociation.Value.Settings, "ComponentGroups", componentGroup.Uid, "Macros", componentAssociation.Key, DefaultFilenames.Settings);
                }
            }
        }
    }
}
