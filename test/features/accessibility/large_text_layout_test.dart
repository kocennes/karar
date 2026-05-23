import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/theme/app_theme.dart';
import 'package:karar/features/feed/post_card.dart';
import 'package:karar/features/post_detail/comment_list.dart';
import 'package:karar/features/post_detail/vote_bar.dart';
import 'package:karar/shared/data/sample_posts.dart';

void main() {
  testWidgets('PostCard fits mobile width with large app text', (tester) async {
    await tester.pumpWidget(
      _LargeTextHarness(
        child: PostCard(
          post: samplePosts.first,
          onTap: () {},
        ),
      ),
    );

    expect(tester.takeException(), isNull);
    expect(find.byType(PostCard), findsOneWidget);
  });

  testWidgets('VoteBar fits compact and detail widths with large app text',
      (tester) async {
    final post = samplePosts.first.copyWith(
      voteCountHakli: 142,
      voteCountHaksiz: 38,
    );

    await tester.pumpWidget(
      _LargeTextHarness(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            VoteBar(post: post, isCompact: true),
            const SizedBox(height: 16),
            VoteBar(post: post),
          ],
        ),
      ),
    );

    expect(tester.takeException(), isNull);
    expect(find.byType(VoteBar), findsNWidgets(2));
  });

  testWidgets('CommentList actions fit mobile width with large app text',
      (tester) async {
    await tester.pumpWidget(
      _LargeTextHarness(
        child: CommentList(
          comments: [
            samplePosts.first.comments.first,
            samplePosts.first.comments.last.copyWith(isOwner: true),
          ],
          postId: samplePosts.first.id,
          onUpvote: (_) {},
          onDelete: (_) {},
          onReply: (_) {},
          onPin: (_) {},
          onUnpin: () {},
          isPostOwner: true,
        ),
      ),
    );

    expect(tester.takeException(), isNull);
    expect(find.byType(CommentList), findsOneWidget);
  });
}

class _LargeTextHarness extends StatelessWidget {
  const _LargeTextHarness({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return ProviderScope(
      child: MaterialApp(
        theme: AppTheme.light(),
        home: MediaQuery(
          data: const MediaQueryData(
            size: Size(360, 800),
            textScaler: TextScaler.linear(1.15),
          ),
          child: Scaffold(
            body: Center(
              child: SizedBox(width: 360, child: child),
            ),
          ),
        ),
      ),
    );
  }
}
