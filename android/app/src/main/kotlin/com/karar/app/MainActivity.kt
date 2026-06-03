package com.karar.app

import android.app.NotificationChannel
import android.app.NotificationManager
import android.os.Build
import android.os.Bundle
import io.flutter.embedding.android.FlutterActivity

class MainActivity : FlutterActivity() {
    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.O) {
            createNotificationChannels()
        }
    }

    private fun createNotificationChannels() {
        val manager = getSystemService(NotificationManager::class.java) ?: return
        val channels = listOf(
            NotificationChannel("comments", "Yorumlar", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "Postlarına gelen yeni yorumlar"
            },
            NotificationChannel("mentions", "Bahsedilmeler", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Yanıtlar ve @etiketlemeler"
            },
            NotificationChannel("milestones", "Kararlar", NotificationManager.IMPORTANCE_DEFAULT).apply {
                description = "Topluluk karar verdi ve viral eşikler"
            },
            NotificationChannel("viral", "Viral", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Trend olan içerikler"
            },
            NotificationChannel("digest", "Özet", NotificationManager.IMPORTANCE_LOW).apply {
                description = "Haftalık içerik özeti"
            },
            NotificationChannel("system", "Sistem", NotificationManager.IMPORTANCE_HIGH).apply {
                description = "Moderasyon ve sistem duyuruları"
            },
        )
        manager.createNotificationChannels(channels)
    }
}
