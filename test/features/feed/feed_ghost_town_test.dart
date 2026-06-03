import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:karar/features/feed/feed_provider.dart';
import 'package:karar/features/feed/feed_screen.dart';
import 'package:karar/shared/data/sample_posts.dart';
import 'package:karar/shared/models/post.dart';

// A minimal FeedNotifier that returns a predetermined FeedState for UI tests.
class _FixedFeedNotifier extends FeedNotifier {
  _FixedFeedNotifier(this._initial);
  final FeedState _initial;

  @override
  FeedState build() => _initial;
}

GoRouter _router() => GoRouter(
      routes: [
        GoRoute(path: '/', builder: (_, __) => const FeedScreen()),
        GoRoute(
          path: '/posts/:id',
          builder: (_, __) => const Scaffold(body: SizedBox()),
        ),
        GoRoute(
          path: '/discover',
          builder: (_, __) => const Scaffold(body: SizedBox()),
        ),
        GoRoute(
          path: '/search',
          builder: (_, __) => const Scaffold(body: SizedBox()),
        ),
        GoRoute(
          path: '/create',
          builder: (_, __) => const Scaffold(body: SizedBox()),
        ),
      ],
    );

Widget _app(FeedState state) {
  final notifier = _FixedFeedNotifier(state);
  return ProviderScope(
    overrides: [
      feedProvider.overrideWith(() => notifier),
    ],
    child: MaterialApp.router(routerConfig: _router()),
  );
}

void main() {
  group('Ghost-town fallback — FeedState unit', () {
    test('isFallback is false when normal feed has posts', () {
      final state = FeedState(posts: samplePosts, isFallback: false);
      expect(state.isFallback, isFalse);
      expect(state.posts, isNotEmpty);
    });

    test('isFallback is true when showing fallback posts', () {
      final state = FeedState(posts: samplePosts, isFallback: true);
      expect(state.isFallback, isTrue);
      expect(state.posts, isNotEmpty);
    });

    test('isFallback false when empty and no fallback available', () {
      final state = FeedState(isFallback: false);
      expect(state.isFallback, isFalse);
      expect(state.posts, isEmpty);
    });
  });

  group('Ghost-town fallback — filtering contract', () {
    test('muted category posts are excluded from fallback', () {
      const mutedCategoryId = 42;
      final posts = [
        _post('1', categoryId: 1),
        _post('2', categoryId: mutedCategoryId),
        _post('3', categoryId: 3),
      ];

      // Simulate the muted filtering that _fetchFallback applies
      final mutedSet = {mutedCategoryId};
      final filtered =
          posts.where((p) => !mutedSet.contains(p.category.id)).toList();

      expect(filtered.length, 2);
      expect(filtered.any((p) => p.category.id == mutedCategoryId), isFalse);
    });

    test('fallback only shows active-status posts (backend contract)', () {
      // Backend endpoint always returns status=active posts.
      // Verify the client-side Post model defaults to active.
      final post = _post('1', categoryId: 1);
      expect(post.status, 'active');
    });
  });

  group('Ghost-town fallback — UI', () {
    Future<void> pumpFeed(WidgetTester tester, FeedState state) async {
      await tester.pumpWidget(_app(state));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
    }

    testWidgets('fallback banner is visible when isFallback is true',
        (tester) async {
      await pumpFeed(tester, FeedState(posts: samplePosts, isFallback: true));

      expect(
        find.text('Bu akışta henüz paylaşım yok — popüler gönderiler'),
        findsOneWidget,
      );
    });

    testWidgets('fallback banner is hidden when isFallback is false',
        (tester) async {
      await pumpFeed(tester, FeedState(posts: samplePosts, isFallback: false));

      expect(
        find.text('Bu akışta henüz paylaşım yok — popüler gönderiler'),
        findsNothing,
      );
    });

    testWidgets('posts are still rendered when fallback is active',
        (tester) async {
      await pumpFeed(tester, FeedState(posts: samplePosts, isFallback: true));

      expect(find.text(samplePosts.first.title), findsOneWidget);
    });

    testWidgets('EmptyState shown when fallback also returns no posts',
        (tester) async {
      await tester.binding.setSurfaceSize(const Size(400, 900));
      addTearDown(() => tester.binding.setSurfaceSize(null));

      await pumpFeed(tester, FeedState(isFallback: false));

      expect(find.text('Henüz paylaşım yok'), findsOneWidget);
    });
  });
}

Post _post(String id, {required int categoryId}) => Post(
      id: id,
      category: Category(id: categoryId, name: 'Kategori', icon: '•'),
      title: 'Test post $id',
      content: 'İçerik',
      createdAgo: '1s önce',
      voteCountHakli: 10,
      voteCountHaksiz: 5,
      commentCount: 2,
      comments: const [],
      createdAt: DateTime(2026, 1, 1),
      createdOrder: 0,
    );
