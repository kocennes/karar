# Flutter keeps native JNI methods automatically via the Flutter Gradle plugin.
# Add rules here only for native Android plugins that need reflection.

# Keep Firebase Crashlytics stack trace info
-keepattributes SourceFile,LineNumberTable
-keep public class * extends java.lang.Exception

# AdMob
-keep class com.google.android.gms.ads.** { *; }

# Play Core (used by Flutter deferred components)
-keep class com.google.android.play.core.** { *; }
