import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:karar/core/theme/app_theme.dart';
import 'package:karar/core/theme/category_style.dart';
import 'package:karar/shared/data/sample_posts.dart';
import 'package:karar/shared/models/post.dart';
import 'package:karar/shared/widgets/glass_rail.dart';
import 'package:karar/shared/widgets/karar_avatar.dart';
import 'package:karar/shared/widgets/karar_button.dart';
import 'package:karar/shared/widgets/karar_chip.dart';
import 'package:karar/shared/widgets/karar_circle_button.dart';
import 'package:karar/shared/widgets/karar_logo.dart';
import 'package:karar/shared/widgets/karar_sheet.dart';
import 'package:karar/shared/widgets/karar_surface.dart';
import 'package:karar/shared/widgets/karar_text_tabs.dart';
import 'package:karar/shared/widgets/on_color_chip.dart';
import 'package:karar/shared/widgets/post_surface.dart';
import 'package:karar/shared/widgets/verdict_chip.dart';
import 'package:karar/shared/widgets/vote_rail.dart';

void main() {
  final textPost = samplePosts.first.copyWith(
    hasImage: false,
    voteCountHakli: 146,
    voteCountHaksiz: 54,
    commentCount: 81,
  );

  for (final dark in [false, true]) {
    for (final scale in [1.0, 1.3]) {
      testWidgets(
          'primitives render without overflow '
          '(${dark ? 'dark' : 'light'}, x$scale)', (tester) async {
        await tester.pumpWidget(
          _Harness(
            dark: dark,
            scale: scale,
            child: ListView(
              padding: const EdgeInsets.all(16),
              children: [
                const KararLogo(),
                const SizedBox(height: 12),
                KararTextTabs(
                  labels: const ['Trend', 'Yeni', 'Tartışmalı'],
                  index: 0,
                  onChanged: (_) {},
                ),
                Wrap(
                  spacing: 8,
                  children: [
                    KararChip(label: 'İş Hayatı', selected: true, onTap: () {}),
                    KararChip(
                      label: 'İlişkiler',
                      icon: CategoryStyle.magenta.icon,
                      onTap: () {},
                      tone: const KararChipTone.category(
                        CategoryStyle.magenta,
                      ),
                    ),
                  ],
                ),
                Row(
                  children: [
                    KararCircleButton(
                      icon: Icons.search,
                      onPressed: () {},
                      tooltip: 'Ara',
                    ),
                    const KararCircleButton(
                      icon: Icons.close_rounded,
                      onPressed: null,
                      tooltip: 'Kapat',
                      style: KararCircleStyle.muted,
                      size: 36,
                    ),
                    const KararAvatar(
                      username: 'ilker',
                      radius: 20,
                      ring: KararAvatarRing.gradient,
                    ),
                    const KararAvatar(
                      username: '',
                      anonymous: true,
                      shape: KararAvatarShape.rounded,
                    ),
                  ],
                ),
                KararSurface(
                  child: Column(
                    children: [
                      const KararSheetHeader(title: 'Yorumlar', count: '58'),
                      KararButton(label: 'Karar Al', onPressed: () {}),
                      const SizedBox(height: 8),
                      KararButton(
                        label: 'Misafir Olarak Dene',
                        onPressed: () {},
                        variant: KararButtonVariant.tonal,
                        size: KararButtonSize.large,
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16),
                PostSurface(
                  post: textPost,
                  child: SizedBox(
                    height: 440,
                    child: Stack(
                      children: [
                        Positioned(
                          left: 20,
                          right: 80,
                          bottom: 18,
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              const OnColorChip(
                                label: 'İlişkiler',
                                icon: Icons.favorite_rounded,
                              ),
                              const SizedBox(height: 12),
                              VerdictChip(post: textPost),
                              const SizedBox(height: 8),
                              VerdictChip(
                                post: textPost.copyWith(
                                  voteCountHakli: 3,
                                  voteCountHaksiz: 2,
                                ),
                                tone: VerdictChipTone.onSurface,
                              ),
                            ],
                          ),
                        ),
                        Positioned(
                          top: 28,
                          right: 0,
                          child: VoteRail(
                            hakliCount: textPost.voteCountHakli,
                            haksizCount: textPost.voteCountHaksiz,
                            commentCount: textPost.commentCount,
                            myVote: VoteType.hakli,
                            onVote: (_) {},
                            onComment: () {},
                            onSave: () {},
                            onShare: () {},
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        );
        await tester.pump(const Duration(milliseconds: 300));
        expect(tester.takeException(), isNull);
        expect(find.byType(GlassRail), findsOneWidget);
        expect(find.text('karar'), findsOneWidget);
        expect(find.text('%73'), findsOneWidget);
        expect(find.text('%27'), findsOneWidget);
      });
    }
  }

  testWidgets('VoteRail reports votes and exposes semantics', (tester) async {
    final votes = <VoteType>[];
    await tester.pumpWidget(
      _Harness(
        child: Center(
          child: VoteRail(
            hakliCount: 12,
            haksizCount: 3,
            commentCount: 4,
            myVote: null,
            onVote: votes.add,
            onComment: () {},
          ),
        ),
      ),
    );
    await tester.tap(find.byKey(const ValueKey('feed_vote_hakli_button')));
    await tester.tap(find.byKey(const ValueKey('feed_vote_haksiz_button')));
    expect(votes, [VoteType.hakli, VoteType.haksiz]);
    expect(find.bySemanticsLabel('Haklı oyu ver'), findsOneWidget);
    expect(find.bySemanticsLabel('Haksız oyu ver'), findsOneWidget);
    expect(find.bySemanticsLabel('Yorumlar, 4'), findsOneWidget);
    // Vote capsules keep a >= 44 px hit area.
    final size =
        tester.getSize(find.byKey(const ValueKey('feed_vote_hakli_button')));
    expect(size.width, greaterThanOrEqualTo(44));
    expect(size.height, greaterThanOrEqualTo(44));
  });

  testWidgets('VoteRail ignores taps while a vote is pending', (tester) async {
    final votes = <VoteType>[];
    await tester.pumpWidget(
      _Harness(
        child: Center(
          child: VoteRail(
            hakliCount: 1,
            haksizCount: 1,
            commentCount: 0,
            myVote: null,
            onVote: votes.add,
            pendingVote: VoteType.hakli,
          ),
        ),
      ),
    );
    await tester.tap(find.byKey(const ValueKey('feed_vote_haksiz_button')));
    await tester.pump(const Duration(milliseconds: 100));
    expect(votes, isEmpty);
  });

  testWidgets('KararCircleButton keeps a 44 px hit area for small visuals',
      (tester) async {
    var taps = 0;
    await tester.pumpWidget(
      _Harness(
        child: Center(
          child: KararCircleButton(
            icon: Icons.more_horiz_rounded,
            onPressed: () => taps++,
            tooltip: 'Gönderi seçenekleri',
            style: KararCircleStyle.onColor,
            size: 32,
          ),
        ),
      ),
    );
    final finder = find.byType(KararCircleButton);
    expect(tester.getSize(finder).width, greaterThanOrEqualTo(44));
    await tester.tapAt(tester.getTopLeft(finder) + const Offset(3, 3));
    expect(taps, 1);
    expect(find.bySemanticsLabel('Gönderi seçenekleri'), findsOneWidget);
  });

  testWidgets('KararTextTabs reports the tapped index', (tester) async {
    int? tapped;
    await tester.pumpWidget(
      _Harness(
        child: KararTextTabs(
          labels: const ['Gönderiler', 'Yorumlar', 'Kaydedilenler'],
          index: 0,
          onChanged: (i) => tapped = i,
        ),
      ),
    );
    await tester.tap(find.text('Yorumlar'));
    expect(tapped, 1);
  });
}

class _Harness extends StatelessWidget {
  const _Harness({required this.child, this.dark = false, this.scale = 1.0});

  final Widget child;
  final bool dark;
  final double scale;

  @override
  Widget build(BuildContext context) {
    return MaterialApp(
      theme: AppTheme.light(),
      darkTheme: AppTheme.dark(),
      themeMode: dark ? ThemeMode.dark : ThemeMode.light,
      home: MediaQuery(
        data: MediaQueryData(
          size: const Size(360, 1600),
          textScaler: TextScaler.linear(scale),
        ),
        child: Scaffold(
          body: SizedBox(width: 360, child: child),
        ),
      ),
    );
  }
}
