using System;

namespace CityBuilderCore
{
    /// <summary>
    /// holds a reference to a structure which may be replaced(for example buildings being replaced when they evolve)<br/>
    /// therefore if a structure is replaced by some other equivalent structure references that other entities have on it dont break
    /// </summary>
    public class StructureReference
    {
        /// <summary>
        /// the currently referenced structure
        /// </summary>
        public IStructure Instance { get; private set; }
        /// <summary>
        /// check if the structure still exists or if it has been terminated
        /// </summary>
        public bool HasInstance => Instance != null && Instance as UnityEngine.Object;

        /// <summary>
        /// fired when the structure gets replaced
        /// </summary>
        public event Action<IStructure, IStructure> Replacing;

        public StructureReference(IStructure instance)
        {
            Instance = instance;
        }

        public void Replace(IStructure replacement)
        {
            Replacing?.Invoke(Instance, replacement);
            Instance = replacement;
            replacement.StructureReference = this;
        }
    }
}