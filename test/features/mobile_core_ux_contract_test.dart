import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('mobile core UX backlog items are wired to documented screens', () {
    final register =
        File('lib/features/auth/register_screen.dart').readAsStringSync();
    final router = File('lib/core/router/app_router.dart').readAsStringSync();
    final homeShell =
        File('lib/features/home/home_shell.dart').readAsStringSync();
    final feed = File('lib/features/feed/feed_screen.dart').readAsStringSync();
    final feedProvider =
        File('lib/features/feed/feed_provider.dart').readAsStringSync();
    final rateLimit =
        File('lib/shared/widgets/rate_limit_ui.dart').readAsStringSync();
    final auth = File('lib/core/auth/auth_service.dart').readAsStringSync();
    final app = File('lib/app.dart').readAsStringSync();
    final postDetail = File('lib/features/post_detail/post_detail_screen.dart')
        .readAsStringSync();
    final createPost = File('lib/features/create_post/create_post_screen.dart')
        .readAsStringSync();
    final createPostProvider =
        File('lib/features/create_post/create_post_provider.dart')
            .readAsStringSync();
    final imagePicker =
        File('lib/features/create_post/image_picker_widget.dart')
            .readAsStringSync();
    final search =
        File('lib/features/search/search_screen.dart').readAsStringSync();

    expect(register, contains('isUsernameAvailable'));
    expect(router, contains('ChangeUsernameSheet.show'));
    expect(homeShell, contains('LoginNudge.show'));
    expect(feedProvider, contains('checkForNewPosts'));
    expect(feed, contains('_NewPostsBanner'));
    expect(homeShell, contains('ConnectivityBanner'));
    expect(feed, contains('_buildDisconnectedBanner'));
    expect(rateLimit, contains('RateLimitedAction.vote'));
    expect(rateLimit, contains('RateLimitedAction.report'));
    expect(auth, contains('LogoutReason.sessionExpired'));
    expect(app, contains('Oturum süren doldu'));
    expect(postDetail, contains('ContentUnavailableView'));
    expect(postDetail, contains('under_review'));
    expect(createPost, contains('PopScope'));
    expect(createPost, contains('saveDraft'));
    expect(createPostProvider, contains('png'));
    expect(createPostProvider, contains('webp'));
    expect(imagePicker, contains('uploadFailed'));
    expect(app, contains('didChangeAppLifecycleState'));
    expect(app, contains('didCrashOnPreviousExecution'));
    expect(search, contains("Tab(text: 'Kullanıcılar')"));
    expect(search, contains("context.push('/users/\${user.username}')"));
  });
}
