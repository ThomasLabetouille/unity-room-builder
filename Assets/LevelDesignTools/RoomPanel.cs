using System;
using System.Collections.Generic;
using UnityEngine;

namespace LevelDesignTools
{
    /// <summary>
    /// Etat logique d'un panneau (mur / sol / plafond) genere par le Room Builder.
    ///
    /// Le mesh affiche n'est qu'une CONSEQUENCE de cet etat : il est entierement reconstruit
    /// a chaque decoupe a partir de <see cref="Size"/> et de la liste des trous. C'est ce qui
    /// permet de decouper plusieurs fois le meme mur : sans cet etat, une fois le Cube d'origine
    /// remplace par un mesh troue, l'outil n'avait plus aucun moyen de retrouver ni les
    /// dimensions du panneau (le localScale est remis a 1 par la decoupe) ni les trous deja
    /// perces, et refusait donc de retravailler la cible.
    ///
    /// Le composant est ajoute automatiquement par le Room Builder lors de la premiere decoupe ;
    /// il n'y a rien a regler a la main.
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("Level Design Tools/Room Panel")]
    public class RoomPanel : MonoBehaviour
    {
        /// <summary>
        /// Un trou perce dans le panneau, decrit par son contour ferme exprime dans le repere
        /// (U,V) du panneau (unites monde, origine au centre du panneau).
        /// </summary>
        [Serializable]
        public class Hole
        {
            public List<Vector2> Points = new List<Vector2>();

            public Hole() { }

            public Hole(IEnumerable<Vector2> points)
            {
                Points = new List<Vector2>(points);
            }
        }

        [Tooltip("Dimensions reelles du panneau sur X, Y et Z (unites monde). Le mesh genere est " +
                 "deja a cette taille : le localScale du transform doit rester a 1.")]
        public Vector3 Size = Vector3.one;

        [Tooltip("Axe local correspondant a la largeur (U) du panneau : 0 = X, 1 = Y, 2 = Z.")]
        public int UAxis = 0;

        [Tooltip("Axe local correspondant a la hauteur (V) du panneau : 0 = X, 1 = Y, 2 = Z.")]
        public int VAxis = 1;

        [Tooltip("Axe local correspondant a l'epaisseur (W) du panneau : 0 = X, 1 = Y, 2 = Z.")]
        public int WAxis = 2;

        [Tooltip("Trous perces dans le panneau, en coordonnees (U,V) locales.")]
        public List<Hole> Holes = new List<Hole>();

        [Tooltip("Contour exterieur du panneau en coordonnees (U,V). Vide pour un panneau " +
                 "rectangulaire, ou il se deduit des dimensions.")]
        public List<Vector2> Outer = new List<Vector2>();

        public float HalfU => Mathf.Abs(Size[UAxis]) * 0.5f;
        public float HalfV => Mathf.Abs(Size[VAxis]) * 0.5f;
        public float HalfW => Mathf.Abs(Size[WAxis]) * 0.5f;
        public float Thickness => Mathf.Abs(Size[WAxis]);

        /// <summary>
        /// Contour exterieur du panneau. Un mur, un sol rectangulaire : le rectangle decrit par
        /// Size. Un sol ou un plafond dessine a main levee : le contour libre stocke dans Outer.
        ///
        /// La liste vide vaut rectangle. C'est ce qui permet aux panneaux crees avant l'ouverture
        /// aux contours libres de continuer a se reconstruire sans rien changer chez eux.
        /// </summary>
        public List<Vector2> ResolveOuterContour()
        {
            if (Outer != null && Outer.Count >= 3) return new List<Vector2>(Outer);

            float hu = HalfU;
            float hv = HalfV;
            return new List<Vector2>
            {
                new Vector2(-hu, -hv),
                new Vector2(hu, -hv),
                new Vector2(hu, hv),
                new Vector2(-hu, hv),
            };
        }

    }
}
