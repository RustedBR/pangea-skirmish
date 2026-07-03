using UnityEngine;

namespace PangeaSkirmish
{
    /// <summary>Cria materiais compatíveis com URP (Unlit) em runtime, com fallback.</summary>
    public static class MaterialFactory
    {
        public static Material UnlitMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            var m = new Material(shader);
            if (m.HasProperty("_Cull"))   m.SetFloat("_Cull",   0f); // dupla face (tiles deitados, ghosts)
            if (m.HasProperty("_ZWrite")) m.SetInt("_ZWrite",   0);  // tiles não escrevem no depth buffer
                                                                      // → sprites sempre renderizam na frente
            return m;
        }

        public static Material Colored(Color c)
        {
            var m = UnlitMaterial();
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }

        /// <summary>Material com iluminação (URP/Lit) para dar volume 2.5D às unidades.</summary>
        public static Material LitColored(Color c)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) return Colored(c);
            var m = new Material(shader);
            m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        /// <summary>Material translúcido com textura (gradiente gerado por código).</summary>
        public static Material TransparentTextured(Texture2D tex)
        {
            var m = UnlitMaterial();
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.color = Color.white;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_BaseMap"))   m.SetTexture("_BaseMap", tex);
            else                             m.mainTexture = tex;
            return m;
        }

        /// <summary>Material translúcido (URP/Unlit transparente) para o "ghost" de planejamento.</summary>
        public static Material TransparentColored(Color c)
        {
            var m = UnlitMaterial();
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f);
            m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.SetInt("_ZWrite", 0);
            m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            m.color = c;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            return m;
        }
    }
}
