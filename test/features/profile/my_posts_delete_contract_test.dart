import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  test('my posts delete flow removes the deleted post from profile state', () {
    final postCard =
        File('lib/features/feed/post_card.dart').readAsStringSync();
    final myPostsScreen =
        File('lib/features/profile/my_posts_screen.dart').readAsStringSync();
    final myPostsProvider =
        File('lib/features/profile/my_posts_provider.dart').readAsStringSync();
    final postDetailProvider =
        File('lib/features/post_detail/post_detail_provider.dart')
            .readAsStringSync();

    expect(postCard, contains('final VoidCallback? onDeleted;'));
    expect(postCard, contains('onDeleted?.call();'));
    expect(myPostsProvider, contains('void removePost(String postId)'));
    expect(myPostsProvider, contains('post.id != postId'));
    expect(myPostsScreen, contains('removePost(post.id)'));
    expect(postDetailProvider, contains("ref.invalidate(myPostsProvider)"));
  });
}
