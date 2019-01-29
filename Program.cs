using System;
using System.Drawing;
using System.Drawing.Imaging;

using geometry;

namespace tinykaboom
{
    class Program
    {
        // All the explosion fits in a sphere with this radius. The center lies in the origin.
        const float SphereRadius = 1.5f;

        // Amount of noise applied to the sphere (towards the center).
        const float NoiseAmplitude = 1.0f;

        static Vec3f yellow = new Vec3f( 1.7f, 1.3f, 1.0f );
        static Vec3f orange = new Vec3f( 1.0f, 0.6f, 0.0f );
        static Vec3f red = new Vec3f( 1.0f, 0.0f, 0.0f );
        static Vec3f darkGray = new Vec3f( 0.2f, 0.2f, 0.2f );
        static Vec3f gray = new Vec3f( 0.4f, 0.4f, 0.4f );

        static T lerp<T>( T v0, T v1, float t ) 
        {
            dynamic dv0 = v0, dv1 = v1;

            return dv0 + ( dv1 - dv0 ) * Math.Max( 0.0f, Math.Min( 1.0f, t ) );
        }

        static float hash( float n )
        {
            var x = Math.Sin(n) * 43758.5453f;

            return ( float ) ( x - Math.Floor(x) );
        }

        static float noise( Vec3f x ) 
        {
            var p = new Vec3f( ( float ) Math.Floor( x.x ), ( float ) Math.Floor( x.y ), ( float ) Math.Floor( x.z ) );
    
            var f = x - p;
    
            f = f * ( f * ( new Vec3f( 3, 3, 3 ) - f * 2.0f ) );

            var n = p * new Vec3f( 1.0f, 57.0f, 113.0f );

            return lerp( lerp(
                             lerp( hash( n +  0.0f ), hash( n +  1.0f ), f.x ),
                             lerp( hash( n + 57.0f ), hash( n + 58.0f ), f.x ), f.y ),
                        lerp(
                            lerp( hash( n + 113.0f ), hash( n + 114.0f ), f.x ),
                            lerp( hash( n + 170.0f ), hash( n + 171.0f ), f.x ), f.y ), f.z );
        }

        static Vec3f rotate( Vec3f v ) 
        {
            return new Vec3f( 
                new Vec3f( 0.0f, 0.8f, 0.6f ) * v, 
                new Vec3f ( -0.80f, 0.36f, -0.48f ) * v, 
                new Vec3f( -0.60f, -0.48f,  0.64f ) * v );
        }

        // TODO: find a better one.
        // This is a bad noise function with lots of artifacts. 
        static float fractal_brownian_motion( Vec3f x ) 
        {
            var p = rotate(x);

            float f = 0;

            f += 0.5000f * noise(p); p = p * 2.32f;
            f += 0.2500f * noise(p); p = p * 3.03f;
            f += 0.1250f * noise(p); p = p * 2.61f;
            f += 0.0625f * noise(p);

            return f / 0.9375f;
        }

        // Simple linear gradent yellow-orange-red-darkgray-gray. d is supposed to vary from 0 to 1.
        // Note that the color is "hot", i.e. has components > 1.
        static Vec3f palette_fire( float d )
        { 
            var x = Math.Max( 0.0f, Math.Min( 1.0f, d ) );

            if ( x < .25f )
                return lerp( gray, darkGray, x * 4.0f );

            else if ( x < .5f )
                return lerp( darkGray, red, x * 4.0f - 1.0f );

            else if ( x < .75f )
                return lerp( red, orange, x * 4.0f - 2.0f );

            return lerp( orange, yellow, x * 4.0f - 3.0f );
        }

        // This function defines the implicit surface we render.
        static float signed_distance( Vec3f p ) 
        { 
            var displacement = -fractal_brownian_motion( p * 3.4f ) * NoiseAmplitude;

            return p.norm() - ( SphereRadius + displacement );
        }

        // Notice the early discard; in fact I know that the noise() function produces non-negative values,
        // thus all the explosion fits in the sphere. Thus this early discard is a conservative check.
        // It is not necessary, just a small speed-up.
        static bool sphere_trace( Vec3f orig, Vec3f dir, ref Vec3f pos )
        {
            if ( orig * orig - Math.Pow( orig * dir, 2 ) > Math.Pow( SphereRadius, 2 ) ) return false;
            
            pos = orig;

            for ( var i = 0; i < 128; i++ )
            {
                var d = signed_distance( pos );

                if ( d < 0 ) return true;

                // Note that the step depends on the current distance,
                // if we are far from the surface, we can do big steps.
                pos = pos + dir * Math.Max( d * 0.1f, .01f );
            }

            return false;
        }

        // Simple finite differences, very sensitive to the choice of the eps constant.
        static Vec3f distance_field_normal( Vec3f pos )
        { 
            var eps = 0.1f;

            var d = signed_distance( pos );
            var nx = signed_distance( pos + new Vec3f( eps, 0, 0 ) ) - d;
            var ny = signed_distance( pos + new Vec3f( 0, eps, 0 ) ) - d;
            var nz = signed_distance( pos + new Vec3f( 0, 0, eps ) ) - d;

            return new Vec3f( nx, ny, nz ).normalize();
        }

        static void Main()
        {
            const int width = 640; // image width
            const int height = 480; // image height

            const float w2 = width / 2.0f;
            const float h2 = height / 2.0f;

            const float fov = 3.1415f / 3.0f; // field of view angle

            var dirz = ( float ) ( -h2 / Math.Tan( fov / 2f ) );

            var framebuffer = new Vec3f[ width * height ];

            // Load background.
            var bmp = ( Bitmap ) Bitmap.FromFile( "envmap.jpg" );

            int pixelSize = 4;

            switch ( bmp.PixelFormat )
            {
                case PixelFormat.Format24bppRgb: pixelSize = 3; break;        
            }

            BitmapData bmpData = null;

            try
            {
                bmpData = bmp.LockBits( new Rectangle( 0, 0, bmp.Width, bmp.Height ), ImageLockMode.ReadOnly, bmp.PixelFormat );

                var kx = .5f * 3.1415f / Math.Atan( w2 / -dirz );

                var ky = .4f * 3.1415f * 2f / fov;

                var x0 = bmp.Width / 4 - ( int ) ( kx * width / 2 );

                var y0 = bmp.Height / 2 - ( int ) ( ky * height / 2 );

                for ( var y = 0; y < height; ++y )
                    unsafe
                    {
                        var row = ( byte * ) bmpData.Scan0 + ( int ) ( y0 + ky * y ) % bmp.Height * bmpData.Stride;

                        for ( var x = 0; x < width; ++x )
                        {
                            var rx = ( int ) ( x0 + kx * x ) * pixelSize % bmpData.Stride;

                            var r = row[ rx + 2 ];
                            var g = row[ rx + 1 ];
                            var b = row[ rx + 0 ];

                            var c = new Vec3f( r, g, b ) / 255f;

                            var i = y * width + x;

                            framebuffer[i] = c;
                        }
                    }
            }
            finally
            {
                if ( bmpData != null ) bmp.UnlockBits( bmpData );
            }

            pixelSize = 4;

            // The camera is placed to (0,0,3) and it looks along the -z axis.
            var vcam = new Vec3f( 0, 0, 3 );
            
            // One light is placed to (10,10,10).
            var vlight = new Vec3f( 10, 10, 10 );
            
            var hit = new Vec3f();            
            
            // Actual rendering loop.
            for ( var j = 0; j < height; j++ )
            { 
                for ( var i = 0; i < width; i++ )
                {
                    // This flips the image at the same time.
                    var dirx = ( i + 0.5f ) - w2;
                    var diry = -( j + 0.5f ) + h2;                    
            
                    var vdir = new Vec3f( dirx, diry, dirz ).normalize();
            
                    if ( sphere_trace( vcam, vdir, ref hit ) )
                    { 
                        var noiseLevel = ( SphereRadius - hit.norm() ) / NoiseAmplitude;
                        
                        var lightDir = ( vlight - hit ).normalize();                     
            
                        var lightIntensity = Math.Max( 0.4f, lightDir * distance_field_normal( hit ) );
            
                        framebuffer[ i + j * width ] = palette_fire( ( -0.2f + noiseLevel ) * 2 ) * lightIntensity;
                    }
                }
            }

            // Save the framebuffer as image.
            bmp = new Bitmap( width, height, PixelFormat.Format32bppArgb );

            try
            {
                bmpData = bmp.LockBits( new Rectangle( 0, 0, width, height ), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb );

                for ( var y = 0; y < height; ++y )
                    unsafe
                    {
                        var targetRow = ( byte * ) bmpData.Scan0 + y * bmpData.Stride;

                        for ( var x = 0; x < width; ++x )
                        {
                            var i = y * width + x;

                            var c = framebuffer[i];

                            var max = Math.Max( c[0], Math.Max( c[1], c[2] ) );

                            if ( max > 1 ) c = c / max;

                            targetRow[ x * pixelSize + 0 ] = ( byte ) ( 255 * Math.Max( 0f, Math.Min( 1f, c[2] ) ) );
                            targetRow[ x * pixelSize + 1 ] = ( byte ) ( 255 * Math.Max( 0f, Math.Min( 1f, c[1] ) ) );
                            targetRow[ x * pixelSize + 2 ] = ( byte ) ( 255 * Math.Max( 0f, Math.Min( 1f, c[0] ) ) );
                            targetRow[ x * pixelSize + 3 ] = 255;
                        }
                    }
            }
            finally
            {
                if ( bmpData != null ) bmp.UnlockBits( bmpData );
            }

            bmp.Save( "out.jpg", ImageFormat.Jpeg );
        }
    }
}
