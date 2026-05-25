import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/theme/app_theme.dart';
import 'package:karar/features/feed/post_card.dart';
import 'package:karar/features/post_detail/comment_list.dart';
import 'package:karar/features/post_detail/vote_bar.dart';
import 'package:karar/shared/data/sample_posts.dart';

void main() {
  // ── 1.15x metin ölçeği (sistem erişilebilirlik ayarı "Büyük") ──────────

  testWidgets('PostCard fits mobile width with large app text', (tester) async {
    await tester.pumpWidget(
      _Harness(
        child: PostCard(post: samplePosts.first, onTap: () {}),
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
      _Harness(
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
      _Harness(
        child: CommentList(
          comments: [
            samplePosts.first.comments.first,
            samplePosts.first.comments.last.copyWith(isOwner: true),
          ],
          postId: samplePosts.first.id,
          onUpvote: (_) {},
          onDownvote: (_) {},
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

  // ── 1.3x metin ölçeği (sistem "En Büyük" / erişilebilirlik modu) ───────

  testWidgets('PostCard fits mobile width with extra-large text (1.3x)',
      (tester) async {
    await tester.pumpWidget(
      _Harness(
        scale: 1.3,
        child: PostCard(post: samplePosts.first, onTap: () {}),
      ),
    );
    expect(tester.takeException(), isNull);
    expect(find.byType(PostCard), findsOneWidget);
  });

  testWidgets('VoteBar has no overflow at 1.3x text scale', (tester) async {
    final post = samplePosts.first.copyWith(
      voteCountHakli: 1234,
      voteCountHaksiz: 567,
    );
    await tester.pumpWidget(
      _Harness(
        scale: 1.3,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            VoteBar(post: post, isCompact: true),
            const SizedBox(height: 8),
            VoteBar(post: post),
          ],
        ),
      ),
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('CommentList has no overflow at 1.3x text scale', (tester) async {
    await tester.pumpWidget(
      _Harness(
        scale: 1.3,
        child: CommentList(
          comments: samplePosts.first.comments,
          postId: samplePosts.first.id,
          onUpvote: (_) {},
          onDownvote: (_) {},
          onDelete: (_) {},
          onReply: (_) {},
          onPin: (_) {},
          onUnpin: () {},
          isPostOwner: true,
        ),
      ),
    );
    expect(tester.takeException(), isNull);
  });

  // ── Dar ekran (320px) + büyük metin ────────────────────────────────────

  testWidgets('PostCard fits narrow screen (320px) with large text',
      (tester) async {
    await tester.pumpWidget(
      _Harness(
        width: 320,
        child: PostCard(post: samplePosts.first, onTap: () {}),
      ),
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets('VoteBar fits narrow screen (320px) with large text',
      (tester) async {
    final post = samplePosts.first.copyWith(
      voteCountHakli: 99,
      voteCountHaksiz: 1,
    );
    await tester.pumpWidget(
      _Harness(
        width: 320,
        child: VoteBar(post: post, isCompact: true),
      ),
    );
    expect(tester.takeException(), isNull);
  });
}

class _Harness extends StatelessWidget {
  const _Harness({
    required this.child,
    this.scale = 1.15,
    this.width = 360,
  });

  final Widget child;
  final double scale;
  final double width;

  @override
  Widget build(BuildContext context) {
    return ProviderScope(
      child: MaterialApp(
        theme: AppTheme.light(),
        home: MediaQuery(
          data: MediaQueryData(
            size: Size(width, 800),
            textScaler: TextScaler.linear(scale),
          ),
          child: Scaffold(
            body: Center(
              child: SizedBox(width: width, child: child),
            ),
          ),
        ),
      ),
    );
  }
}
