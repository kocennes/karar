import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

import 'package:karar/features/feed/feed_screen.dart';
import 'package:karar/features/search/search_screen.dart';
import 'package:karar/shared/data/sample_posts.dart';

void main() {
  testWidgets('FeedScreen renders sample posts in demo mode', (tester) async {
    final router = GoRouter(
      routes: [
        GoRoute(
          path: '/',
          builder: (_, __) => const FeedScreen(),
        ),
        GoRoute(
          path: '/posts/:id',
          builder: (_, state) => Scaffold(
            appBar: AppBar(title: const Text('Post detayı')),
            body: const SizedBox(),
          ),
        ),
      ],
    );

    await tester.pumpWidget(
      ProviderScope(
        child: MaterialApp.router(routerConfig: router),
      ),
    );
    await tester.pump(const Duration(milliseconds: 400));

    expect(find.text('karar'), findsOneWidget);
    expect(find.text(samplePosts.first.title), findsOneWidget);
  });

  testWidgets('FeedScreen search filters posts locally', (tester) async {
    final router = GoRouter(
      routes: [
        GoRoute(path: '/', builder: (_, __) => const FeedScreen()),
        GoRoute(path: '/search', builder: (_, __) => const SearchScreen()),
        GoRoute(
          path: '/posts/:id',
          builder: (_, __) => const Scaffold(body: SizedBox()),
        ),
      ],
    );

    await tester.pumpWidget(
      ProviderScope(
        child: MaterialApp.router(routerConfig: router),
      ),
    );
    await tester.pump(const Duration(milliseconds: 400));

    await tester.tap(find.byIcon(Icons.search));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byType(TextField),
      'XYZ_TERM_THAT_MATCHES_NOTHING',
    );
    await tester.pump(const Duration(milliseconds: 350));

    expect(find.text('Sonuç bulunamadı.'), findsOneWidget);
  });
}
