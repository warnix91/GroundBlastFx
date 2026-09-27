Shader "GroundBlastFx/GroundDebris"
{
    // Particules calculées sur GPU (VolumeField.compute, noyau Debris), dans le repère FIXE du sol du foyer :
    // - vide : grains d'éjectas balistiques, fines traînées orientées selon la vitesse (flou de mouvement) ;
    // - eau : gouttes d'embruns lancées en couronne, qui retombent dans l'eau (blanches, plus épaisses, courtes).
    // Largeur au moins ~1 pixel à toute distance, opacité réduite d'autant (pas de scintillement de loin).
    SubShader
    {
        Tags { "Queue"="Transparent+101" "RenderType"="Transparent" }
        Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            CGPROGRAM
            #pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "GEFlow.cginc"
            struct DebrisParticle { float4 posLife; float4 velSeed; };
            StructuredBuffer<DebrisParticle> _GEDebris;
            float4 _GEColorA[4], _GEColorB[4], _GEParams[4];
            float4 _GEExtra[4];   // air raréfié, nappe, mode des particules (1 grains, 2 gouttes, 3 gravillons)
            float4 _GECameraRight, _GESunDir, _GESunColor, _GEAmbientSky;
            int _GEDebrisPerSlot, _GESurfaceGroup;
            sampler3D _GEField;
            float4 _GEFieldDims;
            float _GESimBlend;
            sampler2D _GEGroundCol;      // couleur réelle du sol (1.8), voir GroundVolume passe 2
            float4 _GEGroundColW[4];

            float3 GroundTint(uint k, float3 palette, float scale)
            {
                float4 g = tex2Dlod(_GEGroundCol, float4((k + 0.5) / 4.0, 0.5, 0, 0));
                return lerp(palette, saturate(g.rgb * scale), saturate(g.a) * _GEGroundColW[k].x);
            }

            // Transmittance du nuage simulé du foyer entre la caméra et le grain (6 échantillons) : un gravillon ou une
            // goutte derrière ou dans un nuage dense en est caché (1.8 : ils restaient nets à travers la poussière).
            float CloudTrans(uint k, float3 world)
            {
                if (_GESimBlend < 0.5) return 1.0;
                float3 east, up, north;
                GEBasis(k, east, up, north);
                float3 o = _WorldSpaceCameraPos - _GEGridO[k].xyz, w = world - _GEGridO[k].xyz;
                float3 a = float3(dot(o, east), dot(o, up), dot(o, north));
                float3 b = float3(dot(w, east), dot(w, up), dot(w, north));
                float tau = 0;
                [unroll] for (int i = 0; i < 6; i++)
                {
                    float3 uvw = GEGridUv(k, lerp(a, b, (i + 0.5) / 6.0));
                    if (all(uvw > 0.0) && all(uvw < 1.0))
                    {
                        float zc = clamp(uvw.z, 0.5 / _GEFieldDims.z, 1.0 - 0.5 / _GEFieldDims.z);
                        tau += tex3Dlod(_GEField, float4(uvw.x, uvw.y, (k + zc) / _GEFieldDims.w, 0)).r;
                    }
                }
                return exp(-tau * (length(b - a) / 6.0) * 0.12 * _GEGridN[k].w);
            }
            struct v2f { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR0; nointerpolation float soft : TEXCOORD1; };

            v2f vert(uint vertexId : SV_VertexID, uint instanceId : SV_InstanceID)
            {
                v2f o;
                uint slot = instanceId / (uint)_GEDebrisPerSlot;
                DebrisParticle p = _GEDebris[instanceId];
                float3 east, up, north;
                GEBasis(slot, east, up, north);
                float3 world = _GEGridO[slot].xyz + east * p.posLife.x + up * p.posLife.y + north * p.posLife.z;
                float3 vel = east * p.velSeed.x + up * p.velSeed.y + north * p.velSeed.z;
                float speed = max(length(vel), 1e-3);
                float3 dir = vel / speed;
                float pmode = _GEExtra[slot].z;
                bool vacuum = _GEParams[slot].w > 0.5 || (pmode > 0.5 && pmode < 1.5);   // grains du vide ou d'air raréfié
                bool water = !vacuum && _GEParams[slot].z > 3.5 && _GEParams[slot].z < 4.5;
                bool gravel = pmode > 2.5;
                // Embruns : un mélange de petites gouttes et de courts paquets flous, sans longues rayures.
                // Le flou de mouvement des grains lunaires conserve sa longueur propre.
                float sprayShape = frac(p.velSeed.w * 17.37);
                float len = water ? clamp(speed * lerp(0.008, 0.020, sprayShape), 0.07, 0.55)
                          : gravel ? clamp(speed * 0.006, 0.06, 0.5)
                                   : clamp(speed * 0.012, 0.05, 6.0);
                float3 toCam = normalize(_WorldSpaceCameraPos - world);
                float3 side = cross(dir, toCam);
                side = dot(side, side) > 1e-6 ? normalize(side) : _GECameraRight.xyz;
                float dist = length(_WorldSpaceCameraPos - world);
                float pixel = dist * 2.0 / max(unity_CameraProjection._m11 * _ScreenParams.y, 1.0);
                // Taille physique (m) : grain du vide ; paquet de gouttes pour les embruns (flou, s'étire en vol).
                float grain = water ? 0.07 + 0.12 * p.velSeed.w : gravel ? 0.05 + 0.14 * p.velSeed.w * p.velSeed.w : 0.012 + 0.03 * p.velSeed.w;
                float width = max(grain, pixel * 1.1);
                float coverage = saturate(grain / width);
                float2 corner = vertexId == 0 ? float2(0, -1) : vertexId == 1 ? float2(1, -1) :
                                vertexId == 2 ? float2(0, 1) : vertexId == 3 ? float2(0, 1) :
                                vertexId == 4 ? float2(1, -1) : float2(1, 1);
                world += -dir * len * (1 - corner.x) + side * (width * 0.5 * corner.y);
                o.pos = UnityWorldToClipPos(world);
                o.uv = corner;
                float alive = step(0.001, p.posLife.w) * ((vacuum || water || gravel) ? 1.0 : 0.0);
                if ((_GESurfaceGroup == 1 && water) || (_GESurfaceGroup == 2 && !water)) alive = 0;
                float fade = saturate(p.posLife.w * 3.0);
                float mu = dot(-toCam, _GESunDir.xyz);
                // Ciel désaturé (1.8 : les gouttes viraient au cyan).
                float3 skyD = lerp(_GEAmbientSky.rgb, dot(_GEAmbientSky.rgb, float3(0.3, 0.5, 0.2)).xxx, 0.6);
                float3 col;
                if (water)
                {
                    // Gouttes : blanches, très lumineuses à contre-jour (diffusion vers l'avant).
                    float phase = 0.6 + 2.2 * pow(saturate(mu * 0.5 + 0.5), 8);
                    col = float3(0.93, 0.95, 0.97) * (_GESunColor.rgb * phase * 0.8 + skyD * 1.2);
                }
                else if (gravel)
                {
                    // Gravillons et mottes : couleur du sol, plus sombre (ombre propre), éclairés par le soleil.
                    // 1.8 : plus sombres (mottes de terre, cailloux), pas des confettis clairs.
                    col = GroundTint(slot, _GEColorB[slot].rgb, 0.78) * 0.5 * (_GESunColor.rgb * (0.45 + 0.4 * saturate(dot(_GESunDir.xyz, up))) + skyD * 0.6);
                }
                else
                {
                    // Grains en plein soleil au-dessus du sol : au moins aussi clairs que le sol éclairé (jamais des traits sombres).
                    // Air raréfié (Duna) : grains de la couleur de la poussière, moins de lueur que dans le vide.
                    bool thinAir = _GEParams[slot].w < 0.5;
                    float phase = 0.9 + (thinAir ? 0.7 : 1.8) * pow(saturate(mu * 0.5 + 0.5), 6);
                    col = saturate(GroundTint(slot, _GEColorA[slot].rgb, 1.08) * (thinAir ? 1.0 : 1.3) + (thinAir ? 0.02 : 0.08)) * (_GESunColor.rgb * phase + skyD);
                }
                // Grains du vide : visibles près de l'impact seulement (loin, la nappe n'est plus qu'une brume de stries).
                float fromImpact = length(p.posLife.xz - _GESrc[slot].xz);
                float riG = max(_GESrc[slot].w, 1.0);
                float nearFade = water || gravel ? 1.0 : smoothstep(1.5 * riG, 4.0 * riG, fromImpact) * (1.0 - smoothstep(8.0 * riG, 25.0 * riG, fromImpact));
                float strength = (water ? 0.5 : gravel ? 0.9 : 0.35 * saturate(_GEParams[slot].x * 1.5 + 0.2)) * nearFade;
                // Cachés par le nuage qu'ils traversent ; effacés tout près de la caméra (gros rectangles sinon).
                float nearCam = smoothstep(1.5, 6.0, dist);
                o.color = float4(col, alive * fade * coverage * strength * nearCam * CloudTrans(slot, world));
                o.soft = water || gravel ? 1.0 : 0.0;   // gravillons : mottes aux bords doux, pas des rectangles
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // Grain : tête nette, queue qui s'efface. Embruns : paquet flou (profil doux sur les deux axes).
                float across = 1 - abs(i.uv.y);
                float a = i.soft > 0.5
                    ? i.color.a * smoothstep(0.0, 0.28, i.uv.x) * (1.0 - smoothstep(0.72, 1.0, i.uv.x)) * across * across
                    : i.color.a * smoothstep(0.0, 0.6, i.uv.x) * saturate(across * 1.8);
                return float4(i.color.rgb, a);
            }
            ENDCG
        }
    }
    Fallback Off
}
