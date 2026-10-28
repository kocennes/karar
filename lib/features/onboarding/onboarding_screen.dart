import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../core/theme/motion.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_chip.dart';
import '../feed/categories_provider.dart';

const _kOnboardingDone = 'onboarding_done';

Future<bool> isOnboardingDone() async {
  final prefs = await SharedPreferences.getInstance();
  return prefs.getBool(_kOnboardingDone) ?? false;
}

Future<void> markOnboardingDone() async {
  final prefs = await SharedPreferences.getInstance();
  await prefs.setBool(_kOnboardingDone, true);
}

/// Onboarding (spec §6.10): three full-bleed gradient steps (violet,
/// magenta, ocean — the dark-tuned stops in dark mode) with white text, then
/// the category step on the canvas. The bottom controls (step pills, CTA,
/// login link) and "Atla" float over the pages and switch between their
/// on-colour and on-canvas looks with the page.
class OnboardingScreen extends ConsumerStatefulWidget {
  const OnboardingScreen({super.key});

  @override
  ConsumerState<OnboardingScreen> createState() => _OnboardingScreenState();
}

class _OnboardingScreenState extends ConsumerState<OnboardingScreen> {
  final _controller = PageController();
  int _page = 0;
  final _selectedCategories = <int>{};

  static const _steps = [
    _OnboardingStep(
      icon: Icons.balance_rounded,
      title: 'Karar nedir?',
      description: 'Gerçek hayat çatışmalarını anlat. Kim haklı, kim haksız? '
          'Topluluk oy versin, karar çıksın.',
    ),
    _OnboardingStep(
      icon: Icons.how_to_vote_rounded,
      title: 'Nasıl oylanır?',
      description: 'Her paylaşıma "Haklı" veya "Haksız" oyu ver. '
          '40 oy sonrasında sonuçlar açılır, topluluk kararını görürsün.',
    ),
    _OnboardingStep(
      icon: Icons.visibility_off_rounded,
      title: 'Tamamen anonim',
      description: 'Hesap açmadan katılabilirsin. Oylar gizli, '
          'kimse kimin ne oyladığını göremez. Sadece dürüst kararlar.',
    ),
  ];

  static const _totalPages = 4; // 3 intro + 1 category

  /// Height reserved under the pages for the floating bottom controls
  /// (pills 8 + 20 + CTA 56 + login link 4 + 44 + vertical padding).
  static const _controlsRoom = 176.0;

  /// Height reserved above the pages for the "Atla" button row.
  static const _topRoom = 60.0;

  void _next() {
    if (_page < _totalPages - 1) {
      if (KararMotion.reduce(context)) {
        _controller.jumpToPage(_page + 1);
      } else {
        _controller.nextPage(
          duration: KararMotion.split,
          curve: KararMotion.emphasized,
        );
      }
    } else {
      _finish();
    }
  }

  Future<void> _finish() async {
    // Save selected categories locally (backend sync happens via FollowedCategoriesNotifier)
    for (final id in _selectedCategories) {
      final followed = ref.read(followedCategoriesProvider);
      if (!followed.contains(id)) {
        await ref.read(followedCategoriesProvider.notifier).toggle(id);
      }
    }
    await markOnboardingDone();
    if (mounted) context.go('/');
  }

  void _toggleCategory(int id) {
    setState(() {
      if (_selectedCategories.contains(id)) {
        _selectedCategories.remove(id);
      } else {
        _selectedCategories.add(id);
      }
    });
  }

  bool get _isLastPage => _page == _totalPages - 1;

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final onColour = _page < _steps.length;
    final ctaLabel = _isLastPage
        ? (_selectedCategories.isEmpty ? 'Başla' : 'Devam')
        : 'Devam';
    final fadeDuration = KararMotion.fade(context, KararMotion.base);

    return AnnotatedRegion<SystemUiOverlayStyle>(
      value: onColour || kc.isDark
          ? SystemUiOverlayStyle.light
          : SystemUiOverlayStyle.dark,
      child: Scaffold(
        backgroundColor: kc.canvas,
        body: Stack(
          children: [
            Positioned.fill(
              child: PageView.builder(
                controller: _controller,
                onPageChanged: (p) => setState(() => _page = p),
                itemCount: _totalPages,
                itemBuilder: (_, i) => i < _steps.length
                    ? _StepPage(
                        step: _steps[i],
                        style: CategoryStyle.cycle(i),
                        topRoom: _topRoom,
                        bottomRoom: _controlsRoom,
                      )
                    : _CategoryStep(
                        selected: _selectedCategories,
                        onToggle: _toggleCategory,
                        topRoom: _topRoom,
                        bottomRoom: _controlsRoom,
                      ),
              ),
            ),

            // "Atla"
            Positioned(
              top: 0,
              right: 0,
              child: SafeArea(
                bottom: false,
                left: false,
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(0, 8, 12, 0),
                  child: TextButton(
                    onPressed: () async {
                      await markOnboardingDone();
                      if (!context.mounted) return;
                      context.go('/');
                    },
                    style: onColour
                        ? TextButton.styleFrom(
                            foregroundColor: kc.onColor,
                            overlayColor: kc.onColor,
                          )
                        : null,
                    child: const Text('Atla'),
                  ),
                ),
              ),
            ),

            // Step pills, CTA, login link.
            Positioned(
              left: 0,
              right: 0,
              bottom: 0,
              child: SafeArea(
                top: false,
                child: Center(
                  child: ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 440),
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(24, 16, 24, 16),
                      child: Column(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          ExcludeSemantics(
                            child: Row(
                              mainAxisAlignment: MainAxisAlignment.center,
                              children: List.generate(
                                _totalPages,
                                (i) => AnimatedContainer(
                                  duration: KararMotion.d(
                                    context,
                                    KararMotion.base,
                                  ),
                                  curve: KararMotion.standard,
                                  margin: const EdgeInsets.symmetric(
                                    horizontal: 4,
                                  ),
                                  width: _page == i ? 24 : 8,
                                  height: 8,
                                  decoration: BoxDecoration(
                                    color: onColour
                                        ? (_page == i
                                            ? kc.onColor
                                            : kc.onColor.withValues(
                                                alpha: 0.42,
                                              ))
                                        : (_page == i
                                            ? kc.primary
                                            : kc.outline),
                                    borderRadius: KararRadius.all(4),
                                  ),
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(height: 20),
                          SizedBox(
                            width: double.infinity,
                            child: AnimatedSwitcher(
                              duration: fadeDuration,
                              child: onColour
                                  ? _CapsuleButton(
                                      key: const ValueKey('cta_on_colour'),
                                      label: ctaLabel,
                                      onPressed: _next,
                                    )
                                  : KararButton(
                                      key: const ValueKey('cta_on_canvas'),
                                      label: ctaLabel,
                                      size: KararButtonSize.large,
                                      onPressed: _next,
                                    ),
                            ),
                          ),
                          if (_isLastPage) ...[
                            const SizedBox(height: 4),
                            TextButton(
                              onPressed: () async {
                                await markOnboardingDone();
                                if (!context.mounted) return;
                                context.go('/auth/login');
                              },
                              child: const Text('Hesabın varsa Giriş Yap'),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// 56 px light capsule CTA on the gradient steps: `capsule` (white in
/// light, the spec off-white #F4F3F8 in dark) with #15131F text (≥ 16:1).
/// Not red: red reads as "Haksız".
class _CapsuleButton extends StatelessWidget {
  const _CapsuleButton({
    super.key,
    required this.label,
    required this.onPressed,
  });

  final String label;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Container(
      width: double.infinity,
      decoration: BoxDecoration(
        borderRadius: KararRadius.all(KararRadius.pill),
        boxShadow: KararShadow.capsule,
      ),
      child: FilledButton(
        onPressed: onPressed,
        style: FilledButton.styleFrom(
          backgroundColor: kc.capsule,
          foregroundColor: kc.onCapsuleText,
          overlayColor: kc.onCapsuleText,
          minimumSize: const Size(64, 56),
          shape: const StadiumBorder(),
          textStyle: AppTypography.buttonL,
          elevation: 0,
        ),
        child: Text(label),
      ),
    );
  }
}

class _CategoryStep extends ConsumerWidget {
  const _CategoryStep({
    required this.selected,
    required this.onToggle,
    required this.topRoom,
    required this.bottomRoom,
  });

  final Set<int> selected;
  final void Function(int) onToggle;
  final double topRoom;
  final double bottomRoom;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final kc = context.kc;
    final categoriesAsync = ref.watch(categoriesProvider);

    return ColoredBox(
      color: kc.canvas,
      child: SafeArea(
        child: Align(
          alignment: Alignment.topCenter,
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 560),
            // The 24 px side padding wraps the header only: the chip grid
            // carries it inside its scroll view, so the viewport clip leaves
            // room for the light chip shadows at the edges.
            child: Padding(
              padding: EdgeInsets.only(top: topRoom),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Padding(
                    padding: const EdgeInsets.symmetric(
                      horizontal: _CategoryGrid.sidePadding,
                    ),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        ExcludeSemantics(
                          child: Container(
                            width: 56,
                            height: 56,
                            decoration: BoxDecoration(
                              color: kc.primaryContainer,
                              borderRadius: KararRadius.all(KararRadius.tile),
                            ),
                            child: Icon(
                              Icons.interests_rounded,
                              size: 28,
                              color: kc.primaryText,
                            ),
                          ),
                        ),
                        const SizedBox(height: 20),
                        Semantics(
                          header: true,
                          child: Text(
                            'Neleri takip etmek istersin?',
                            style:
                                AppTypography.titleXL.copyWith(color: kc.text1),
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Seçtiğin kategoriler akışında öne çıkar. İstediğin zaman değiştirebilirsin.',
                          style: AppTypography.bodyL.copyWith(color: kc.text2),
                        ),
                        // 24 px to the chips: 16 here + the grid's 8 px top
                        // padding.
                        const SizedBox(height: 24 - _CategoryGrid.topPadding),
                      ],
                    ),
                  ),
                  Expanded(
                    child: categoriesAsync.when(
                      data: (categories) => _CategoryGrid(
                        categories: categories,
                        selected: selected,
                        onToggle: onToggle,
                        bottomRoom: bottomRoom,
                      ),
                      loading: () =>
                          const Center(child: CircularProgressIndicator()),
                      error: (_, __) => Padding(
                        padding: const EdgeInsets.symmetric(
                          horizontal: _CategoryGrid.sidePadding,
                        ),
                        child: Center(
                          child: Text(
                            'Kategoriler yüklenemedi.',
                            style: AppTypography.body.copyWith(color: kc.text2),
                          ),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _CategoryGrid extends StatelessWidget {
  const _CategoryGrid({
    required this.categories,
    required this.selected,
    required this.onToggle,
    required this.bottomRoom,
  });

  /// Page side padding of the category step.
  static const double sidePadding = 24;

  /// Room above the first chip row inside the scroll view (shadow tail).
  static const double topPadding = KararSpace.s8;

  final List<Category> categories;
  final Set<int> selected;
  final void Function(int) onToggle;
  final double bottomRoom;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(
        sidePadding,
        topPadding,
        sidePadding,
        bottomRoom,
      ),
      child: Wrap(
        spacing: KararSpace.s8,
        runSpacing: KararSpace.s4,
        children: categories.map((cat) {
          final isSelected = selected.contains(cat.id);
          final style = CategoryStyle.of(cat);
          // Names only: `cat.icon` holds short words ("Aşk", "İş"), not
          // glyphs, so it is never rendered as text.
          return KararChip(
            label: cat.name,
            icon: isSelected ? style.icon : style.iconOutlined,
            selected: isSelected,
            tone: KararChipTone.category(style),
            onCanvas: true,
            onTap: () => onToggle(cat.id),
          );
        }).toList(),
      ),
    );
  }
}

class _OnboardingStep {
  const _OnboardingStep({
    required this.icon,
    required this.title,
    required this.description,
  });

  final IconData icon;
  final String title;
  final String description;
}

/// One full-bleed gradient step: 120 px white-25 % disc with a 56 px white
/// glyph, 30/800 white title, 16/500 white body (white on every dark-tuned
/// stop ≥ 5.88:1).
class _StepPage extends StatelessWidget {
  const _StepPage({
    required this.step,
    required this.style,
    required this.topRoom,
    required this.bottomRoom,
  });

  final _OnboardingStep step;
  final CategoryStyle style;
  final double topRoom;
  final double bottomRoom;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final brightness = Theme.of(context).brightness;

    return Stack(
      fit: StackFit.expand,
      children: [
        ExcludeSemantics(
          child: DecoratedBox(
            decoration: BoxDecoration(
              gradient: style.gradientFor(brightness: brightness),
            ),
          ),
        ),
        ExcludeSemantics(
          child: DecoratedBox(
            decoration: BoxDecoration(
              gradient: kc.isDark
                  ? KararGradients.cardSheenDark
                  : KararGradients.cardSheen,
            ),
          ),
        ),
        SafeArea(
          child: Padding(
            padding: EdgeInsets.fromLTRB(32, topRoom, 32, bottomRoom),
            child: Center(
              child: SingleChildScrollView(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 420),
                  child: Column(
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      ExcludeSemantics(
                        child: Container(
                          width: 120,
                          height: 120,
                          decoration: BoxDecoration(
                            color: kc.onColorSoft,
                            shape: BoxShape.circle,
                          ),
                          child: Icon(step.icon, size: 56, color: kc.onColor),
                        ),
                      ),
                      const SizedBox(height: 32),
                      Semantics(
                        header: true,
                        child: Text(
                          step.title,
                          textAlign: TextAlign.center,
                          style: AppTypography.display.copyWith(
                            fontSize: 30,
                            height: 36 / 30,
                            color: kc.onColor,
                          ),
                        ),
                      ),
                      const SizedBox(height: 16),
                      Text(
                        step.description,
                        textAlign: TextAlign.center,
                        style: AppTypography.bodyL.copyWith(
                          fontWeight: FontWeight.w500,
                          color: kc.onColor,
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
