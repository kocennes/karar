import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../core/theme/app_colors.dart';
import '../../shared/models/post.dart';
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
      emoji: '⚖️',
      title: 'Karar nedir?',
      description: 'Gerçek hayat çatışmalarını anlat. Kim haklı, kim haksız? '
          'Topluluk oy versin, karar çıksın.',
    ),
    _OnboardingStep(
      emoji: '✅❌',
      title: 'Nasıl oylanır?',
      description: 'Her paylaşıma "Haklı" veya "Haksız" oyu ver. '
          '40 oy sonrasında sonuçlar açılır, topluluk kararını görürsün.',
    ),
    _OnboardingStep(
      emoji: '🕶️',
      title: 'Tamamen anonim',
      description: 'Hesap açmadan katılabilirsin. Oylar gizli, '
          'kimse kimin ne oyladığını göremez. Sadece dürüst kararlar.',
    ),
  ];

  static const _totalPages = 4; // 3 intro + 1 category

  void _next() {
    if (_page < _totalPages - 1) {
      _controller.nextPage(
        duration: const Duration(milliseconds: 350),
        curve: Curves.easeOutCubic,
      );
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
    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            Align(
              alignment: Alignment.topRight,
              child: TextButton(
                onPressed: () async {
                  await markOnboardingDone();
                  if (!context.mounted) return;
                  context.go('/');
                },
                child: const Text('Atla'),
              ),
            ),
            Expanded(
              child: PageView.builder(
                controller: _controller,
                onPageChanged: (p) => setState(() => _page = p),
                itemCount: _totalPages,
                itemBuilder: (_, i) => i < _steps.length
                    ? _StepPage(step: _steps[i])
                    : _CategoryStep(
                        selected: _selectedCategories,
                        onToggle: _toggleCategory,
                      ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 24, vertical: 20),
              child: Column(
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    children: List.generate(
                      _totalPages,
                      (i) => AnimatedContainer(
                        duration: const Duration(milliseconds: 250),
                        margin: const EdgeInsets.symmetric(horizontal: 4),
                        width: _page == i ? 24 : 8,
                        height: 8,
                        decoration: BoxDecoration(
                          color:
                              _page == i ? AppColors.accent : AppColors.border,
                          borderRadius: BorderRadius.circular(4),
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 20),
                  SizedBox(
                    width: double.infinity,
                    child: FilledButton(
                      onPressed: _next,
                      style: FilledButton.styleFrom(
                        backgroundColor: AppColors.accent,
                        padding: const EdgeInsets.symmetric(vertical: 16),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(12),
                        ),
                      ),
                      child: Text(
                        _isLastPage
                            ? (_selectedCategories.isEmpty
                                ? 'Başla'
                                : 'Devam')
                            : 'Devam',
                        style: const TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w700,
                        ),
                      ),
                    ),
                  ),
                  if (_isLastPage) ...[
                    const SizedBox(height: 12),
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
          ],
        ),
      ),
    );
  }
}

class _CategoryStep extends ConsumerWidget {
  const _CategoryStep({
    required this.selected,
    required this.onToggle,
  });

  final Set<int> selected;
  final void Function(int) onToggle;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final categoriesAsync = ref.watch(categoriesProvider);

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 24),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const SizedBox(height: 16),
          const Text(
            '🎯',
            style: TextStyle(fontSize: 56),
          ),
          const SizedBox(height: 20),
          Text(
            'Neleri takip etmek istersin?',
            style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                  fontWeight: FontWeight.w900,
                  color: AppColors.textPrimary,
                ),
          ),
          const SizedBox(height: 8),
          Text(
            'Seçtiğin kategoriler akışında öne çıkar. İstediğin zaman değiştirebilirsin.',
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: AppColors.textSecondary,
                  height: 1.5,
                ),
          ),
          const SizedBox(height: 24),
          Expanded(
            child: categoriesAsync.when(
              data: (categories) => _CategoryGrid(
                categories: categories,
                selected: selected,
                onToggle: onToggle,
              ),
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (_, __) => const Center(
                child: Text(
                  'Kategoriler yüklenemedi.',
                  style: TextStyle(color: AppColors.textSecondary),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _CategoryGrid extends StatelessWidget {
  const _CategoryGrid({
    required this.categories,
    required this.selected,
    required this.onToggle,
  });

  final List<Category> categories;
  final Set<int> selected;
  final void Function(int) onToggle;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      child: Wrap(
        spacing: 10,
        runSpacing: 10,
        children: categories.map((cat) {
          final isSelected = selected.contains(cat.id);
          return FilterChip(
            label: Text('${cat.icon} ${cat.name}'),
            selected: isSelected,
            onSelected: (_) => onToggle(cat.id),
            selectedColor: AppColors.accent.withValues(alpha: 0.18),
            checkmarkColor: AppColors.accent,
            labelStyle: TextStyle(
              fontWeight:
                  isSelected ? FontWeight.w700 : FontWeight.normal,
              color: isSelected ? AppColors.accent : null,
            ),
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(20),
              side: BorderSide(
                color: isSelected
                    ? AppColors.accent
                    : Theme.of(context).colorScheme.outlineVariant,
              ),
            ),
          );
        }).toList(),
      ),
    );
  }
}

class _OnboardingStep {
  const _OnboardingStep({
    required this.emoji,
    required this.title,
    required this.description,
  });

  final String emoji;
  final String title;
  final String description;
}

class _StepPage extends StatelessWidget {
  const _StepPage({required this.step});
  final _OnboardingStep step;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 32),
      child: Column(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          Text(
            step.emoji,
            style: const TextStyle(fontSize: 72),
          ),
          const SizedBox(height: 32),
          Text(
            step.title,
            style: Theme.of(context).textTheme.headlineMedium?.copyWith(
                  fontWeight: FontWeight.w900,
                  color: AppColors.textPrimary,
                ),
            textAlign: TextAlign.center,
          ),
          const SizedBox(height: 16),
          Text(
            step.description,
            style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                  color: AppColors.textSecondary,
                  height: 1.6,
                ),
            textAlign: TextAlign.center,
          ),
        ],
      ),
    );
  }
}
