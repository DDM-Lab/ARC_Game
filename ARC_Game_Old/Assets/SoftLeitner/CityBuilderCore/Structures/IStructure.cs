using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace CityBuilderCore
{
    /// <summary>
    /// interface for anything placed on the map(roads, decorators, buildings, ....)
    /// </summary>
    public interface IStructure : IKeyed
    {
        /// <summary>
        /// reference to the structure that keeps working even if the structure is replaced
        /// </summary>
        StructureReference StructureReference { get; set; }

        /// <summary>
        /// whether the structure can be removed by the player
        /// </summary>
        bool IsDestructible { get; }
        /// <summary>
        /// whether the structure can be moved by the <see cref="MoveTool"/>
        /// </summary>
        bool IsMovable { get; }
        /// <summary>
        /// whether the structure is automatically removed when something is built on top of it
        /// </summary>
        bool IsDecorator { get; }
        /// <summary>
        /// whether walkers can pass the points of this structure
        /// </summary>
        bool IsWalkable { get; }

        /// <summary>
        /// the structure level mask of this structure<br/>
        /// determines which levels a structure occupies<br/>
        /// structures that have no levels in common can be placed on top of each other
        /// </summary>
        int Level { get; }

        /// <summary>
        /// has to be fired when a structures points changed so the manager can readjust paths<br/>
        /// only viable for structures that are stored in list form like underlying, collections and tiles<br/>
        /// other structures have to be reregistered
        /// </summary>
        event Action<PointsChanged<IStructure>> PointsChanged;

        /// <summary>
        /// retrieves the name of the structure for display in the UI
        /// </summary>
        /// <returns>name of the structure for UI</returns>
        string GetName();

        /// <summary>
        /// retrieves all the points the structure occupies
        /// </summary>
        /// <returns>the points the structure occupies</returns>
        IEnumerable<Vector2Int> GetPoints();
        /// <summary>
        /// checks if the structure occupies a certain point
        /// </summary>
        /// <param name="point">a point on the map</param>
        /// <returns>true if the structure occupies that point</returns>
        bool HasPoint(Vector2Int point);

        /// <summary>
        /// adds points to a structure, for example a tree to a tree structure collection<br/>
        /// this may not be possible for structures with fixed points like buildings<br/>
        /// typically called in a structures Start method
        /// </summary>
        /// <param name="points">the points that will be added to the structure</param>
        void Add(IEnumerable<Vector2Int> points);
        /// <summary>
        /// removes points from the structure, for some structures like buildings removing any point will remove the whole thing<br/>
        /// typically called in a structures OnDestroy method
        /// </summary>
        /// <param name="points">the points to remove</param>
        void Remove(IEnumerable<Vector2Int> points);

        /// <summary>
        /// removes a point from one structure and adds it to another while trying to carry over its look<br/>
        /// both structures need to be <see cref="StructureCollectionFloat"/>, <see cref="StructureTerrainTrees"/> or <see cref="StructureTerrainTreeVariants"/>)
        /// </summary>
        /// <param name="a">point will be removed from this structure</param>
        /// <param name="b">point will be added to this structure</param>
        /// <param name="point">point on the map</param>
        /// <param name="keepRandomization">transfer over any size/color randomizations, only works when the structure are of the same type</param>
        /// <param name="keepVariant">transfer over the variant index</param>
        public static void ReplacePoints(IStructure a, IStructure b, Vector2Int point, bool keepRandomization = true, bool keepVariant = true)
        {
            if (a == null || b == null)
                return;

            if (keepRandomization)
            {
                if (a is StructureCollectionFloat collectionFloatA && b is StructureCollectionFloat collectionFloatB)
                {
                    var structure = collectionFloatA.GetObject(point);

                    int? variantIndex = null;
                    if (keepVariant && collectionFloatA.Variants.Length > 1 && collectionFloatA.Variants.Length == collectionFloatB.Variants.Length)
                        variantIndex = collectionFloatA.GetVariantIndex(structure.name);

                    collectionFloatA.Remove(point);
                    collectionFloatB.Add(point, structure.transform.localPosition, structure.transform.localRotation, structure.transform.localScale, variantIndex);
                    return;
                }

                if (a is StructureTerrainTrees treesA && b is StructureTerrainTrees treesB)
                {
                    var tree = treesA.Get(point);

                    treesA.Remove(point);
                    treesB.Add(point, tree);
                    return;
                }

                if (a is StructureTerrainTreeVariants treeVariantsA && b is StructureTerrainTreeVariants treeVariantsB)
                {
                    var tree = treeVariantsA.Get(point);

                    int? variantIndex = null;
                    if (keepVariant && treeVariantsA.Variants.Length > 1 && treeVariantsA.Variants.Length == treeVariantsB.Variants.Length)
                        variantIndex = treeVariantsA.GetVariantIndex(tree);

                    treeVariantsA.Remove(point);
                    treeVariantsB.Add(point, tree, variantIndex);
                    return;
                }
            }
            else
            {
                int variantCountA = 0;
                int variantCountB = 0;

                if (a is StructureCollectionFloat collectionFloatA)
                    variantCountA = collectionFloatA.Variants.Length;
                else if (a is StructureTerrainTreeVariants treesA)
                    variantCountA = treesA.Variants.Length;

                if (b is StructureCollectionFloat collectionFloatB)
                    variantCountB = collectionFloatB.Variants.Length;
                else if (b is StructureTerrainTreeVariants treesB)
                    variantCountB = treesB.Variants.Length;

                if (variantCountA > 1 && variantCountA == variantCountB)
                {
                    int variantIndex = 0;

                    if (a is StructureCollectionFloat aCollectionFloat)
                    {
                        variantIndex = aCollectionFloat.GetVariantIndex(point);
                        aCollectionFloat.Remove(point);
                    }
                    else if (a is StructureTerrainTreeVariants aTrees)
                    {
                        variantIndex = aTrees.GetVariantIndex(point);
                        aTrees.Remove(point);
                    }

                    if (b is StructureCollectionFloat bCollectionFloat)
                    {
                        bCollectionFloat.Add(point, variantIndex);
                    }
                    else if (b is StructureTerrainTreeVariants bTrees)
                    {
                        bTrees.Add(point, variantIndex);
                    }
                    return;
                }
            }

            a.Remove(point);
            b.Add(point);
        }
        /// <summary>
        /// removes points from one structure and adds them to another while trying to carry over its look<br/>
        /// both structures need to be <see cref="StructureCollectionFloat"/>, <see cref="StructureTerrainTrees"/> or <see cref="StructureTerrainTreeVariants"/>)<br/>
        /// used in town so bushes keep their look(size, rotation, color) when berries grown on them<br/>
        /// would also carry the variant index for trees in town if there were any
        /// </summary>
        /// <param name="a">point will be removed from this structure</param>
        /// <param name="b">point will be added to this structure</param>
        /// <param name="points">points on the map</param>
        /// <param name="keepRandomization">transfer over any size/color randomizations, only works when the structure are of the same type</param>
        /// <param name="keepVariant">transfer over the variant index</param>
        public static void ReplacePoints(IStructure a, IStructure b, IEnumerable<Vector2Int> points, bool keepRandomization = true, bool keepVariant = true)
        {
            if (a == null || b == null)
                return;

            if (keepRandomization)
            {
                if (a is StructureCollectionFloat collectionFloatA && b is StructureCollectionFloat collectionFloatB)
                {
                    if (keepVariant && collectionFloatA.Variants.Length > 1 && collectionFloatA.Variants.Length == collectionFloatB.Variants.Length)
                    {
                        var structures = points.Select(p =>
                        {
                            var o = collectionFloatA.GetObject(p);
                            var i = collectionFloatA.GetVariantIndex(o.name);

                            return Tuple.Create(p, o, i);
                        }).ToArray();

                        collectionFloatA.Remove(points);
                        foreach (var structure in structures)
                        {
                            collectionFloatB.Add(structure.Item1, structure.Item2.transform.localPosition, structure.Item2.transform.localRotation, structure.Item2.transform.localScale, structure.Item3);
                        }
                    }
                    else
                    {
                        var structures = points.Select(p => Tuple.Create(p, collectionFloatA.GetObject(p))).ToArray();

                        collectionFloatA.Remove(points);
                        foreach (var structure in structures)
                        {
                            collectionFloatB.Add(structure.Item1, structure.Item2.transform.localPosition, structure.Item2.transform.localRotation, structure.Item2.transform.localScale);
                        }
                    }
                    return;
                }

                if (a is StructureTerrainTrees treesA && b is StructureTerrainTrees treesB)
                {
                    var trees = points.Select(p => Tuple.Create(p, treesA.Get(p))).ToArray();

                    treesA.Remove(points);
                    foreach (var tree in trees)
                    {
                        treesB.Add(tree.Item1, tree.Item2);
                    }
                    return;
                }

                if (a is StructureTerrainTreeVariants treeVariantsA && b is StructureTerrainTreeVariants treeVariantsB)
                {
                    if (keepVariant && treeVariantsA.Variants.Length > 1 && treeVariantsA.Variants.Length == treeVariantsB.Variants.Length)
                    {
                        var trees = points.Select(p =>
                        {
                            var t = treeVariantsA.Get(p);
                            var i = treeVariantsA.GetVariantIndex(t);

                            return Tuple.Create(p, t, i);
                        }).ToArray();
                        
                        treeVariantsA.Remove(points);
                        foreach (var tree in trees)
                        {
                            treeVariantsB.Add(tree.Item1, tree.Item2,tree.Item3);
                        }
                    }
                    else
                    {
                        var trees = points.Select(p => Tuple.Create(p, treeVariantsA.Get(p))).ToArray();

                        treeVariantsA.Remove(points);
                        foreach (var tree in trees)
                        {
                            treeVariantsB.Add(tree.Item1, tree.Item2);
                        }
                    }
                    return;
                }
            }
            else
            {
                int variantCountA = 0;
                int variantCountB = 0;

                if (a is StructureCollectionFloat collectionFloatA)
                    variantCountA = collectionFloatA.Variants.Length;
                else if (a is StructureTerrainTreeVariants treesA)
                    variantCountA = treesA.Variants.Length;

                if (b is StructureCollectionFloat collectionFloatB)
                    variantCountB = collectionFloatB.Variants.Length;
                else if (b is StructureTerrainTreeVariants treesB)
                    variantCountB = treesB.Variants.Length;

                if (variantCountA > 1 && variantCountA == variantCountB)
                {
                    Tuple<Vector2Int,int>[] pointIndices = null;

                    if (a is StructureCollectionFloat aCollectionFloat)
                    {
                        pointIndices = points.Select(p => Tuple.Create(p, aCollectionFloat.GetVariantIndex(p))).ToArray();
                        aCollectionFloat.Remove(points);
                    }
                    else if (a is StructureTerrainTreeVariants aTreeVariants)
                    {
                        pointIndices = points.Select(p => Tuple.Create(p, aTreeVariants.GetVariantIndex(p))).ToArray();
                        aTreeVariants.Remove(points);
                    }

                    if (b is StructureCollectionFloat bCollectionFloat)
                    {
                        foreach (var pointIndex in pointIndices)
                        {
                            bCollectionFloat.Add(pointIndex.Item1, pointIndex.Item2);
                        }
                    }
                    else if (b is StructureTerrainTreeVariants bTreeVariants)
                    {
                        foreach (var pointIndex in pointIndices)
                        {
                            bTreeVariants.Add(pointIndex.Item1, pointIndex.Item2);
                        }
                    }
                    return;
                }
            }

            a.Remove(points);
            b.Add(points);
        }
    }
}