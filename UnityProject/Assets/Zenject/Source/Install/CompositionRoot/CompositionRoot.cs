#if !ZEN_NOT_UNITY3D

using System;
using System.Collections.Generic;
using System.Linq;
using ModestTree;
using UnityEngine;
using UnityEngine.Serialization;
using Zenject.Internal;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zenject
{
    public abstract class CompositionRoot : MonoBehaviour
    {
        [FormerlySerializedAs("Installers")]
        [SerializeField]
        List<MonoInstaller> _installers = new List<MonoInstaller>();

        [SerializeField]
        List<MonoInstaller> _installerPrefabs = new List<MonoInstaller>();

        [SerializeField]
        List<ScriptableObjectInstaller> _scriptableObjectInstallers = new List<ScriptableObjectInstaller>();

        List<Installer> _normalInstallers = new List<Installer>();

        public IEnumerable<MonoInstaller> Installers
        {
            get
            {
                return _installers;
            }
            set
            {
                _installers.Clear();
                _installers.AddRange(value);
            }
        }

        public IEnumerable<MonoInstaller> InstallerPrefabs
        {
            get
            {
                return _installerPrefabs;
            }
            set
            {
                _installerPrefabs.Clear();
                _installerPrefabs.AddRange(value);
            }
        }

        public IEnumerable<ScriptableObjectInstaller> ScriptableObjectInstallers
        {
            get
            {
                return _scriptableObjectInstallers;
            }
            set
            {
                _scriptableObjectInstallers.Clear();
                _scriptableObjectInstallers.AddRange(value);
            }
        }

        // Unlike other installer types this has to be set through code
        public IEnumerable<Installer> NormalInstallers
        {
            get
            {
                return _normalInstallers;
            }
            set
            {
                _normalInstallers.Clear();
                _normalInstallers.AddRange(value);
            }
        }

        void CheckInstallerPrefabTypes()
        {
            foreach (var installer in _installers)
            {
                Assert.IsNotNull(installer, "Found null installer in CompositionRoot '{0}'", this.name);

#if UNITY_EDITOR
                Assert.That(PrefabUtility.GetPrefabType(installer.gameObject) != PrefabType.Prefab,
                    "Found prefab with name '{0}' in the Installer property of CompositionRoot '{1}'.  You should use the property 'InstallerPrefabs' for this instead.", installer.name, this.name);
#endif
            }

            foreach (var installerPrefab in _installerPrefabs)
            {
                Assert.IsNotNull(installerPrefab, "Found null prefab in CompositionRoot");

#if UNITY_EDITOR
                Assert.That(PrefabUtility.GetPrefabType(installerPrefab.gameObject) == PrefabType.Prefab,
                    "Found non-prefab with name '{0}' in the InstallerPrefabs property of CompositionRoot '{1}'.  You should use the property 'Installer' for this instead",
                    installerPrefab.name, this.name);
#endif
                Assert.That(installerPrefab.GetComponent<MonoInstaller>() != null,
                    "Expected to find component with type 'MonoInstaller' on given installer prefab '{0}'", installerPrefab.name);
            }
        }

        protected void InstallInstallers(DiContainer container)
        {
            InstallInstallers(container, new Dictionary<Type, List<TypeValuePair>>());
        }

        // We pass in the container here instead of using our own for validation to work
        protected void InstallInstallers(
            DiContainer container, Dictionary<Type, List<TypeValuePair>> extraArgsMap)
        {
            CheckInstallerPrefabTypes();

            var newGameObjects = new List<GameObject>();
            var allInstallers = _normalInstallers.Cast<IInstaller>()
                .Concat(_installers.Cast<IInstaller>()).Concat(_scriptableObjectInstallers.Cast<IInstaller>()).ToList();

            foreach (var installerPrefab in _installerPrefabs)
            {
                Assert.IsNotNull(installerPrefab, "Found null installer prefab in '{0}'", this.GetType().Name());

                var installerGameObject = GameObject.Instantiate(installerPrefab.gameObject);

                newGameObjects.Add(installerGameObject);

                installerGameObject.transform.SetParent(this.transform, false);
                var installer = installerGameObject.GetComponent<MonoInstaller>();

                Assert.IsNotNull(installer, "Could not find installer component on prefab '{0}'", installerPrefab.name);

                allInstallers.Add(installer);
            }

            foreach (var installer in allInstallers)
            {
                List<TypeValuePair> extraArgs;

                Assert.IsNotNull(installer,
                    "Found null installer in '{0}'", this.GetType().Name());

                if (extraArgsMap.TryGetValue(installer.GetType(), out extraArgs))
                {
                    extraArgsMap.Remove(installer.GetType());
                    container.InstallExplicit(installer, extraArgs);
                }
                else
                {
                    container.Install(installer);
                }
            }
        }

        protected void InstallSceneBindings(DiContainer container)
        {
            foreach (var binding in GetInjectableComponents().OfType<ZenjectBinding>())
            {
                if (binding == null)
                {
                    continue;
                }

                if (binding.CompositionRoot == null)
                {
                    InstallZenjectBinding(container, binding);
                }
            }

            // We'd prefer to use GameObject.FindObjectsOfType<ZenjectBinding>() here
            // instead but that doesn't find inactive gameobjects
            foreach (var binding in Resources.FindObjectsOfTypeAll<ZenjectBinding>())
            {
                if (binding == null)
                {
                    continue;
                }

                if (binding.CompositionRoot == this)
                {
                    InstallZenjectBinding(container, binding);
                }
            }
        }

        protected void InstallZenjectBinding(
            DiContainer container, ZenjectBinding binding)
        {
            if (!binding.enabled)
            {
                return;
            }

            if (binding.Components == null || binding.Components.IsEmpty())
            {
                Log.Warn("Found empty list of components on ZenjectBinding on object '{0}'", binding.name);
                return;
            }

            string identifier = null;

            if (binding.Identifier.Trim().Length > 0)
            {
                identifier = binding.Identifier;
            }

            foreach (var component in binding.Components)
            {
                var bindType = binding.BindType;

                if (component == null)
                {
                    Log.Warn("Found null component in ZenjectBinding on object '{0}'", binding.name);
                    continue;
                }

                switch (bindType)
                {
                    case ZenjectBinding.BindTypes.Self:
                    {
                        container.Bind(identifier, component.GetType()).FromInstance(component);
                        break;
                    }
                    case ZenjectBinding.BindTypes.AllInterfaces:
                    {
                        container.BindAllInterfaces(identifier, component.GetType()).FromInstance(component);
                        break;
                    }
                    case ZenjectBinding.BindTypes.AllInterfacesAndSelf:
                    {
                        container.BindAllInterfacesAndSelf(identifier, component.GetType()).FromInstance(component);
                        break;
                    }
                    default:
                    {
                        throw Assert.CreateException();
                    }
                }
            }
        }

        public abstract void InstallBindings(DiContainer container);

        public abstract IEnumerable<Component> GetInjectableComponents();

        public static IEnumerable<Component> GetInjectableComponents(GameObject gameObject)
        {
            foreach (var component in ZenUtilInternal.GetInjectableComponentsBottomUp(gameObject, true))
            {
                if (component == null)
                {
                    // This warning about fiBackupSceneStorage appears in normal cases so just ignore
                    // Not sure what it is
                    if (gameObject.name != "fiBackupSceneStorage")
                    {
                        Log.Warn("Zenject: Found null component on game object '{0}'.  Possible missing script.", gameObject.name);
                    }
                    continue;
                }

                if (component.GetType().DerivesFrom<MonoInstaller>())
                {
                    // Do not inject on installers since these are always injected before they are installed
                    continue;
                }

                yield return component;
            }
        }
    }
}

#endif