// CHANGE TRACKER: [CHANGE_COUNT: 10] - Added multiple controllable smoke points with texture movement
// Ctrl+Z to count 9 to restore previous state, or count 0 for original
Shader "Unlit/StarShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _StarColor ("Star Color", Color) = (1, 1, 1, 1)

        _BackgroundColor ("Background Color", Color) = (0, 0, 0.1, 1)
        _StarPoints ("Star Points", Range(3, 12)) = 5
        _StarBrightness ("Star Brightness", Range(0.1, 5)) = 2
        _PointSharpness ("Point Sharpness", Range(0.1, 5)) = 2
        _InnerRadius ("Inner Radius", Range(0.1, 0.9)) = 0.4
        _StarRotation ("Star Rotation", Range(0, 360)) = 0
        _LineThickness ("Line Thickness", Range(0.005, 1)) = 0.02
        _ExpansionSpeed ("Expansion Speed", Range(0.1, 5)) = 1
        _StarInterval ("Star Interval", Range(0.1, 2)) = 0.5
        _StartSize ("Start Size", Range(0.01, 1)) = 0.02
        _MaxSize ("Max Size", Range(0.5, 3)) = 1.5
        _FadeStart ("Fade Start", Range(0.1, 1)) = 0.8
        _WaveAmplitude ("Wave Amplitude", Range(0, 0.05)) = 0.01
        _WaveFrequency ("Wave Frequency", Range(1, 20)) = 8
        _WaveSpeed ("Wave Speed", Range(0.1, 5)) = 2
        _RandomSeed ("Random Seed", Range(0, 100)) = 42
        
        // Animation controls
        _PulseSpeed ("Pulse Speed", Range(0.1, 10)) = 2
        _PulseAmplitude ("Pulse Amplitude", Range(0, 0.3)) = 0.1
        _TwistSpeed ("Twist Speed", Range(0, 5)) = 1
        _TwistAmplitude ("Twist Amplitude", Range(0, 0.1)) = 0.02

        _GlowIntensity ("Glow Intensity", Range(0, 2)) = 0.5
        _GlowRadius ("Glow Radius", Range(0.1, 10)) = 0.8
        
        // Emission lines
        _EmissionColor ("Emission Color", Color) = (0.2, 0.8, 1.0, 1)
        _EmissionIntensity ("Emission Intensity", Range(0, 2)) = 0.8
        _EmissionSpeed ("Emission Speed", Range(0.1, 5)) = 1.5
        _EmissionLength ("Emission Length", Range(0.1, 2)) = 0.8
        _EmissionThickness ("Emission Thickness", Range(0.001, 0.05)) = 0.01
        _EmissionCount ("Emission Count", Range(3, 20)) = 8
        _EmissionOffset ("Emission Offset", Range(0.1, 1)) = 0.3
        _EmissionWaveAmplitude ("Emission Wave Amplitude", Range(0, 0.1)) = 0.02
        _EmissionWaveFrequency ("Emission Wave Frequency", Range(1, 20)) = 8
        
        // Center smoke effect
        _SmokeColor ("Smoke Color", Color) = (0.8, 0.8, 0.8, 1)
        _SmokeIntensity ("Smoke Intensity", Range(0, 2)) = 0.5
        _SmokeSpeed ("Smoke Speed", Range(0.1, 5)) = 1.0
        _SmokeScale ("Smoke Scale", Range(0.1, 5)) = 2.0
        _SmokeReach ("Smoke Reach", Range(0.1, 2)) = 0.8
        _SmokeTurbulence ("Smoke Turbulence", Range(0.1, 5)) = 2.0
        _SmokeFlowSpeed ("Smoke Flow Speed", Range(0.1, 3)) = 1.0
        _SmokeFlowDirection ("Smoke Flow Direction", Range(0, 360)) = 45
        
        // Multiple smoke points
        _SmokePoint1 ("Smoke Point 1", Vector) = (0.3, 0.3, 0, 0)
        _SmokePoint2 ("Smoke Point 2", Vector) = (0.7, 0.3, 0, 0)
        _SmokePoint3 ("Smoke Point 3", Vector) = (0.5, 0.7, 0, 0)
        _SmokePoint4 ("Smoke Point 4", Vector) = (0.2, 0.8, 0, 0)
        _SmokePoint5 ("Smoke Point 5", Vector) = (0.8, 0.8, 0, 0)
        
        // Smoke point properties
        _SmokePoint1Intensity ("Smoke Point 1 Intensity", Range(0, 2)) = 0.8
        _SmokePoint2Intensity ("Smoke Point 2 Intensity", Range(0, 2)) = 0.6
        _SmokePoint3Intensity ("Smoke Point 3 Intensity", Range(0, 2)) = 0.7
        _SmokePoint4Intensity ("Smoke Point 4 Intensity", Range(0, 2)) = 0.5
        _SmokePoint5Intensity ("Smoke Point 5 Intensity", Range(0, 2)) = 0.9
        
        // Texture movement
        _SmokeTextureOffset ("Smoke Texture Offset", Vector) = (0, 0, 0, 0)
        _SmokeTextureScale ("Smoke Texture Scale", Range(0.1, 5)) = 1.0

        // Performance
        _FrameRate ("Update Rate (fps)", Range(1, 60)) = 30

        // UI compatibility
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        LOD 100

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        ColorMask [_ColorMask]
        
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _StarColor;

            fixed4 _BackgroundColor;
            float _StarPoints;
            float _StarBrightness;
            float _PointSharpness;
            float _InnerRadius;
            float _StarRotation;
            float _LineThickness;
            float _ExpansionSpeed;
            float _StarInterval;
            float _StartSize;
            float _MaxSize;
            float _FadeStart;
            float _WaveAmplitude;
            float _WaveFrequency;
            float _WaveSpeed;
            float _RandomSeed;
            
            // Animation variables
            float _PulseSpeed;
            float _PulseAmplitude;
            float _TwistSpeed;
            float _TwistAmplitude;

            float _GlowIntensity;
            float _GlowRadius;
            
            // Emission line variables
            fixed4 _EmissionColor;
            float _EmissionIntensity;
            float _EmissionSpeed;
            float _EmissionLength;
            float _EmissionThickness;
            float _EmissionCount;
            float _EmissionOffset;
            float _EmissionWaveAmplitude;
            float _EmissionWaveFrequency;
            
            // Smoke effect variables
            fixed4 _SmokeColor;
            float _SmokeIntensity;
            float _SmokeSpeed;
            float _SmokeScale;
            float _SmokeReach;
            float _SmokeTurbulence;
            float _SmokeFlowSpeed;
            float _SmokeFlowDirection;
            
            // Multiple smoke point variables
            float4 _SmokePoint1;
            float4 _SmokePoint2;
            float4 _SmokePoint3;
            float4 _SmokePoint4;
            float4 _SmokePoint5;
            
            float _SmokePoint1Intensity;
            float _SmokePoint2Intensity;
            float _SmokePoint3Intensity;
            float _SmokePoint4Intensity;
            float _SmokePoint5Intensity;
            
            // Texture movement variables
            float4 _SmokeTextureOffset;
            float _SmokeTextureScale;

            // Performance
            float _FrameRate;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            // Hash function for pseudo-random numbers
            float hash(float n)
            {
                return frac(sin(n + _RandomSeed) * 43758.5453);
            }

            // Rotate a 2D point around origin
            float2 rotate2D(float2 pos, float angle)
            {
                float rad = angle * 0.0174533; // Convert degrees to radians
                float cosA = cos(rad);
                float sinA = sin(rad);
                return float2(
                    pos.x * cosA - pos.y * sinA,
                    pos.x * sinA + pos.y * cosA
                );
            }

            // Star shape with pulse and twist effects
            float starShape(float2 uv, float2 center, float size, float points, float layerIndex, float t)
            {
                float2 pos = uv - center;
                
                // Apply rotation
                pos = rotate2D(pos, _StarRotation);
                
                // Add pulse effect
                float pulseTime = t * _PulseSpeed;
                float pulse = sin(pulseTime + layerIndex * 0.5) * _PulseAmplitude;
                size *= (1.0 + pulse);
                
                // Add twist effect
                float twistTime = t * _TwistSpeed;
                float twist = sin(twistTime + layerIndex * 0.3) * _TwistAmplitude;
                float twistAngle = twist * length(pos);
                pos = rotate2D(pos, twistAngle);
                
                float dist = length(pos);
                float angle = atan2(pos.y, pos.x);
                
                // Normalize angle to 0-2π
                angle = angle + 3.14159;
                
                // Calculate which segment of the star we're in
                float segmentAngle = 6.28318 / points; // 2π / points
                float localAngle = fmod(angle, segmentAngle);
                float halfSegment = segmentAngle * 0.5;
                
                // Create sharp points by using abs and power functions
                float pointFactor = abs(localAngle - halfSegment) / halfSegment;
                pointFactor = pow(pointFactor, _PointSharpness);
                
                // Calculate base radius: full size at points, reduced at valleys
                float currentRadius = size * lerp(1.0, _InnerRadius, pointFactor);
                
                // Add smooth wavy distortion to the radius
                float waveTime = t * _WaveSpeed;
                
                // Create smoother random phase offset for each layer
                float randomPhase = hash(layerIndex) * 6.28318; // 0 to 2π
                
                // Primary wave with smooth random phase
                float primaryWave = sin(angle * _WaveFrequency + waveTime + randomPhase);
                
                // Secondary wave with different frequency and smooth random phase
                float secondaryPhase = hash(layerIndex + 100.0) * 6.28318;
                float secondaryWave = sin(angle * (_WaveFrequency * 1.7) + (waveTime * 0.8) + secondaryPhase);
                
                // Smooth random amplitude multiplier for each layer
                float randomAmplitude = 0.7 + hash(layerIndex + 200.0) * 0.3; // 0.7 to 1.0 (less variation)
                
                // Combine waves with smoother amplitude
                float totalWave = (primaryWave + secondaryWave * 0.3) * _WaveAmplitude * randomAmplitude * size;
                
                // Apply wave distortion to radius
                currentRadius += totalWave;
                
                 // Create hollow star outline
                 float outerEdge = 1.0 - smoothstep(currentRadius - _LineThickness * 0.6, currentRadius + _LineThickness * 0.1, dist);
                 float innerEdge = smoothstep(currentRadius - _LineThickness * 1.2, currentRadius - _LineThickness * 0.8, dist);
                 
                 // Combine to create outline
                 float starMask = outerEdge * innerEdge;
                
                return starMask;
            }

            

                                                   // Generate glow effect
             float glowEffect(float2 uv, float2 center, float size, float layerIndex)
             {
                 float2 pos = uv - center;
                 float dist = length(pos);
                 
                 // Create soft glow around the star
                 float glowSize = size * _GlowRadius;
                 float glow = 1.0 - smoothstep(0.0, glowSize, dist);
                 glow = pow(glow, 2.0); // Soften the glow
                 
                 return glow * _GlowIntensity;
             }
             
             // Generate emission lines from star edges
             float emissionLines(float2 uv, float2 center, float size, float points, float layerIndex, float t)
             {
                 float2 pos = uv - center;
                 
                 // Apply rotation
                 pos = rotate2D(pos, _StarRotation);
                 
                 float dist = length(pos);
                 float angle = atan2(pos.y, pos.x);
                 
                 // Normalize angle to 0-2π
                 angle = angle + 3.14159;
                 
                 // Calculate which segment of the star we're in
                 float segmentAngle = 6.28318 / points; // 2π / points
                 float localAngle = fmod(angle, segmentAngle);
                 float halfSegment = segmentAngle * 0.5;
                 
                 // Create sharp points by using abs and power functions
                 float pointFactor = abs(localAngle - halfSegment) / halfSegment;
                 pointFactor = pow(pointFactor, _PointSharpness);
                 
                 // Calculate base radius: full size at points, reduced at valleys
                 float currentRadius = size * lerp(1.0, _InnerRadius, pointFactor);
                 
                 // Add smooth wavy distortion to the radius (same as star shape)
                 float waveTime = t * _WaveSpeed;
                 float randomPhase = hash(layerIndex) * 6.28318;
                 float primaryWave = sin(angle * _WaveFrequency + waveTime + randomPhase);
                 float secondaryPhase = hash(layerIndex + 100.0) * 6.28318;
                 float secondaryWave = sin(angle * (_WaveFrequency * 1.7) + (waveTime * 0.8) + secondaryPhase);
                 float randomAmplitude = 0.7 + hash(layerIndex + 200.0) * 0.3;
                 float totalWave = (primaryWave + secondaryWave * 0.3) * _WaveAmplitude * randomAmplitude * size;
                 currentRadius += totalWave;
                 
                 // Calculate emission line direction (perpendicular to star edge)
                 float2 emissionDir = normalize(pos);
                 
                                   // Create multiple emission lines around the star
                  float totalEmission = 0.0;
                  float time = t * _EmissionSpeed;
                  
                  for (int i = 0; i < _EmissionCount; i++)
                  {
                      // Calculate emission line angle
                      float emissionAngle = (float(i) / _EmissionCount) * 6.28318;
                      
                      // Add offset to emission start position
                      float2 emissionStart = center + emissionDir * (currentRadius + _EmissionOffset * size);
                      
                      // Calculate line direction (perpendicular to star edge)
                      float2 lineDir = float2(cos(emissionAngle), sin(emissionAngle));
                      
                      // Add waviness to the line direction
                      float waveTime = t * _EmissionSpeed;
                      float waveOffset = sin(waveTime + i * 0.5) * _EmissionWaveAmplitude;
                      float waveAngle = sin(waveTime * _EmissionWaveFrequency + i * 0.3) * _EmissionWaveAmplitude;
                      
                      // Apply wave distortion to line direction
                      float2 waveDir = float2(cos(waveAngle), sin(waveAngle));
                      lineDir = normalize(lineDir + waveDir * waveOffset);
                      
                      // Calculate distance from emission line
                      float2 toPoint = pos - emissionStart;
                      float projection = dot(toPoint, lineDir);
                      float perpendicularDist = length(toPoint - projection * lineDir);
                      
                      // Create sharp line with animation
                      float lineLength = _EmissionLength * size;
                      float animatedLength = lineLength * (0.5 + 0.5 * sin(time + i * 0.5));
                      
                      // Check if point is within animated line
                      float lineMask = 1.0 - smoothstep(0.0, animatedLength, projection);
                      lineMask *= 1.0 - smoothstep(0.0, _EmissionThickness, perpendicularDist);
                      
                      // Add pulsing animation
                      float pulse = sin(time * 2.0 + i * 0.3) * 0.5 + 0.5;
                      lineMask *= pulse;
                      
                      totalEmission += lineMask;
                  }
                 
                 return totalEmission * _EmissionIntensity;
             }
             
                          // Generate smoke from a single point
             float generateSmokeFromPoint(float2 uv, float2 smokePoint, float intensity, float t)
             {
                 float2 pos = uv - smokePoint;
                 float dist = length(pos);
                 float angle = atan2(pos.y, pos.x);
                 
                 // Convert flow direction to radians
                 float flowAngle = _SmokeFlowDirection * 0.0174533;
                 
                 // Create flow direction vector
                 float2 flowDir = float2(cos(flowAngle), sin(flowAngle));
                 
                 // Calculate flow-aligned coordinates
                 float2 flowUV = float2(
                     dot(pos, flowDir),
                     dot(pos, float2(-flowDir.y, flowDir.x))
                 );
                 
                 // Apply texture offset and scale
                 flowUV += _SmokeTextureOffset.xy;
                 flowUV *= _SmokeTextureScale;
                 
                 float time = t * _SmokeFlowSpeed;
                 
                 // Create turbulent flow lines
                 float smoke = 0.0;
                 
                 // Multiple turbulent layers with different frequencies
                 for (int i = 1; i <= 4; i++)
                 {
                     float layerFreq = float(i) * 0.5;
                     float layerSpeed = time * layerFreq;
                     
                     // Create turbulent distortion
                     float2 turbulentOffset = float2(
                         sin(flowUV.x * layerFreq * 2.0 + layerSpeed) * cos(flowUV.y * layerFreq * 1.5 + layerSpeed * 0.7),
                         cos(flowUV.x * layerFreq * 1.5 + layerSpeed * 0.8) * sin(flowUV.y * layerFreq * 2.0 + layerSpeed * 1.2)
                     ) * _SmokeTurbulence * 0.1;
                     
                     // Add flow-aligned noise
                     float2 noisePos = (flowUV + turbulentOffset) * _SmokeScale * layerFreq;
                     float noise = sin(noisePos.x + layerSpeed) * cos(noisePos.y + layerSpeed * 0.6);
                     
                     // Create flow lines with varying intensity
                     float flowLine = sin(flowUV.x * layerFreq * 3.0 + layerSpeed) * 0.5 + 0.5;
                     flowLine *= sin(flowUV.y * layerFreq * 2.0 + layerSpeed * 0.8) * 0.5 + 0.5;
                     
                     // Combine noise and flow lines
                     float layerSmoke = noise * flowLine * (1.0 / float(i));
                     smoke += layerSmoke;
                 }
                 
                 // Add swirling motion around the smoke point
                 float swirl = sin(angle * 8.0 + time * 0.5) * cos(dist * 4.0 - time * 0.3);
                 smoke += swirl * 0.2;
                 
                 // Radial fade from smoke point
                 float radialFade = 1.0 - smoothstep(0.0, _SmokeReach * 0.5, dist);
                 smoke *= radialFade;
                 
                 // Create a circular mask to keep smoke away from center
                 float centerMask = smoothstep(0.0, 0.1, dist);
                 smoke *= centerMask;
                 
                 // Add flow direction influence
                 float flowInfluence = dot(normalize(pos), flowDir) * 0.5 + 0.5;
                 smoke *= flowInfluence;
                 
                 return smoke * intensity;
             }
             
             // Generate background smoke effect with multiple controllable points
             float backgroundSmoke(float2 uv, float2 center, float t)
             {
                 float totalSmoke = 0.0;
                 
                 // Generate smoke from each point
                 totalSmoke += generateSmokeFromPoint(uv, _SmokePoint1.xy, _SmokePoint1Intensity, t);
                 totalSmoke += generateSmokeFromPoint(uv, _SmokePoint2.xy, _SmokePoint2Intensity, t);
                 totalSmoke += generateSmokeFromPoint(uv, _SmokePoint3.xy, _SmokePoint3Intensity, t);
                 totalSmoke += generateSmokeFromPoint(uv, _SmokePoint4.xy, _SmokePoint4Intensity, t);
                 totalSmoke += generateSmokeFromPoint(uv, _SmokePoint5.xy, _SmokePoint5Intensity, t);
                 
                 return totalSmoke * _SmokeIntensity;
             }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                float2 center = float2(0.5, 0.5);
                
                 // Quantize time to target update rate (reduces animation update frequency for performance)
                 float quantTime = floor(_Time.y * _FrameRate) / _FrameRate;
                 
                 // Start with background smoke
                 float backgroundSmokeValue = backgroundSmoke(uv, center, quantTime);
                 fixed4 finalColor = _BackgroundColor + _SmokeColor * backgroundSmokeValue;
                 
                 float totalStarBrightness = 0.0;
                 float totalGlow = 0.0;
                 float totalEmission = 0.0;
                
                // Current time
                float time = quantTime * _ExpansionSpeed;
                
                // Create infinite expanding star layers with smoother transitions
                for (int layer = 0; layer < 15; layer++)
                {
                    // Calculate when this star layer started
                    float layerStartTime = float(layer) * _StarInterval;
                    float layerAge = time - layerStartTime;
                    
                    // Calculate the total cycle time
                    float totalCycleTime = 15.0 * _StarInterval;
                    
                    // Mobile-safe cycle: layerAge is at most -14*_StarInterval and totalCycleTime is 15*_StarInterval,
                    // so one addition always brings it into positive range before fmod.
                    layerAge = fmod(layerAge + totalCycleTime, totalCycleTime);
                    
                    // Calculate current size with smoother growth curve
                    float growthTime = 4.0 * _StarInterval; // Total time to grow
                    float sizeProgress = saturate(layerAge / growthTime);
                    
                    // Use smoother exponential curve
                    float currentSize = _StartSize + (smoothstep(0.0, 1.0, sizeProgress) * (_MaxSize - _StartSize));
                    
                    // Only render if within reasonable size and time
                    if (layerAge <= growthTime && currentSize >= _StartSize)
                    {
                                                 // Generate star shape
                         float starValue = starShape(uv, center, currentSize, _StarPoints, float(layer), quantTime);
                         
                         // Generate glow effect
                         float glowValue = glowEffect(uv, center, currentSize, float(layer));
                         
                         // Generate emission lines
                         float emissionValue = emissionLines(uv, center, currentSize, _StarPoints, float(layer), quantTime);
                         

                         
                         if (starValue > 0.0)
                         {
                             // Smooth fade out as star gets bigger
                             float fadeProgress = sizeProgress;
                             float fadeFactor = 1.0;
                             
                             if (fadeProgress > _FadeStart)
                             {
                                 float fadeRange = 1.0 - _FadeStart;
                                 float localFade = (fadeProgress - _FadeStart) / fadeRange;
                                 fadeFactor = 1.0 - smoothstep(0.0, 1.0, localFade);
                             }
                             
                             // Apply brightness and smooth fade
                             starValue *= _StarBrightness * fadeFactor;
                             glowValue *= fadeFactor;
                             
                                                                                                                     totalStarBrightness += starValue;
                             totalGlow += glowValue;
                             totalEmission += emissionValue * fadeFactor;
                         }

                    }
                }
                
                 // Simple color blending - star color on top of background
                 finalColor.rgb += _StarColor.rgb * totalStarBrightness;
                 finalColor.rgb += _StarColor.rgb * totalGlow * 0.5;
                 finalColor.rgb += _EmissionColor.rgb * totalEmission;
                 
                                   // Ensure we don't exceed maximum brightness
                 finalColor.rgb = min(finalColor.rgb, 1.0);
                
                 // Calculate alpha
                 finalColor.a = _BackgroundColor.a + saturate(totalStarBrightness) * _StarColor.a;
                
                return finalColor;
            }
            ENDCG
        }
    }
}