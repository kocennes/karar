import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'package:karar/features/feed/feed_provider.dart';
import 'package:karar/features/feed/feed_screen.dart';
import 'package:karar/shared/data/sample_posts.dart';

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
  return ProviderScope(
    overrides: [
      feedProvider.overrideWith(() => _FixedFeedNotifier(state)),
    ],
    child: MaterialApp.router(routerConfig: _router()),
  );
}

void main() {
  setUp(() {
    SharedPreferences.setMockInitialValues({});
  });

  tearDown(() {
    final binding = TestWidgetsFlutterBinding.ensureInitialized();
    binding.window.clearPhysicalSizeTestValue();
    binding.window.clearDevicePixelRatioTestValue();
  });

  Future<void> pumpFeed(WidgetTester tester, FeedState state) async {
    tester.view.physicalSize = const Size(900, 1200);
    tester.view.devicePixelRatio = 1;
    await tester.pumpWidget(_app(state));
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));
  }

  testWidgets('first user sees first vote coachmark', (tester) async {
    await pumpFeed(tester, FeedState(posts: samplePosts));

    expect(find.text('Ilk oyunu at'), findsOneWidget);
  });

  testWidgets('coachmark is hidden after voting', (tester) async {
    await pumpFeed(tester, FeedState(posts: samplePosts));

    await tester
        .tap(find.byKey(const ValueKey('feed_vote_hakli_button')).first);
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.text('Ilk oyunu at'), findsNothing);

    await pumpFeed(tester, FeedState(posts: samplePosts));

    expect(find.text('Ilk oyunu at'), findsNothing);
  });

  testWidgets('coachmark is hidden after dismiss', (tester) async {
    await pumpFeed(tester, FeedState(posts: samplePosts));

    await tester.tap(
      find.byKey(const ValueKey('first_vote_coachmark_dismiss')),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(find.text('Ilk oyunu at'), findsNothing);

    await pumpFeed(tester, FeedState(posts: samplePosts));

    expect(find.text('Ilk oyunu at'), findsNothing);
  });

  testWidgets('coachmark is hidden when feed is empty', (tester) async {
    await pumpFeed(tester, FeedState());

    expect(find.text('Ilk oyunu at'), findsNothing);
  });
}
