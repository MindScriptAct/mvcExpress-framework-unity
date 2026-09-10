using System;

namespace mvcExpress.Plugins
{
    /// <summary>
    /// Describes one extra actor-registry row a module wants its Inspector to show, beyond
    /// the built-in Services/Model/Controller/View rows. Pure data - no UnityEditor
    /// dependency, so it's safe to declare from Runtime code (e.g. a module class) and read
    /// from Editor code without either assembly needing to reference the other's internals.
    /// </summary>
    public readonly struct ActorRegistryRowDescriptor
    {
        /// <summary>Serialized field name on the module holding the reference to the child component.</summary>
        public string PropertyName { get; }

        /// <summary>Row label and default child GameObject name shown in the Inspector.</summary>
        public string Label { get; }

        /// <summary>Component type the row's object field and Create button target.</summary>
        public Type ComponentType { get; }

        /// <summary>Desired sibling index among the module's children once created.</summary>
        public int SiblingIndex { get; }

        public ActorRegistryRowDescriptor(string propertyName, string label, Type componentType, int siblingIndex)
        {
            PropertyName = propertyName;
            Label = label;
            ComponentType = componentType;
            SiblingIndex = siblingIndex;
        }
    }

    /// <summary>
    /// Implemented by an MvcModule subclass that wants extra actor-registry rows in its
    /// Inspector beyond the built-in Services/Model/Controller/View, without
    /// MvcModuleEditor needing any knowledge of the subclass's domain. MvcModuleEditor
    /// checks for this interface via a plain "target is IHasAdditionalActorRegistries"
    /// pattern match - no subclassing of MvcModuleEditor required.
    /// </summary>
    public interface IHasAdditionalActorRegistries
    {
        ActorRegistryRowDescriptor[] AdditionalActorRegistries { get; }
    }
}
