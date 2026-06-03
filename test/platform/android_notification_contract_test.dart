import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('Android notification channels match the mobile push contract', () {
    final manifest =
        File('android/app/src/main/AndroidManifest.xml').readAsStringSync();
    final activity = File(
      'android/app/src/main/kotlin/com/karar/app/MainActivity.kt',
    ).readAsStringSync();

    expect(
      manifest,
      contains('android.permission.POST_NOTIFICATIONS'),
      reason: 'Android 13+ requires runtime notification permission.',
    );

    expect(
        activity, contains('Build.VERSION.SDK_INT >= Build.VERSION_CODES.O'));
    expect(activity, contains('createNotificationChannels()'));
    expect(activity, contains('manager.createNotificationChannels(channels)'));

    expect(
      activity,
      contains(
        'NotificationChannel("comments", "Yorumlar", NotificationManager.IMPORTANCE_DEFAULT)',
      ),
    );
    expect(
      activity,
      contains(
        'NotificationChannel("mentions", "Bahsedilmeler", NotificationManager.IMPORTANCE_HIGH)',
      ),
    );
    expect(
      activity,
      contains(
        'NotificationChannel("milestones", "Kararlar", NotificationManager.IMPORTANCE_DEFAULT)',
      ),
    );
    expect(
      activity,
      contains(
        'NotificationChannel("viral", "Viral", NotificationManager.IMPORTANCE_LOW)',
      ),
    );
    expect(
      activity,
      contains(
        'NotificationChannel("system", "Sistem", NotificationManager.IMPORTANCE_HIGH)',
      ),
    );
    expect(
      activity,
      contains(
        'NotificationChannel("digest", "Özet", NotificationManager.IMPORTANCE_LOW)',
      ),
    );
  });
}
