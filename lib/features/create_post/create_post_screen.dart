import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../core/layout/breakpoints.dart';
import '../../core/providers.dart';
import '../../core/storage/post_draft_service.dart';
import '../../core/theme/app_typography.dart';
import '../../core/theme/category_style.dart';
import '../../core/theme/karar_tokens.dart';
import '../../shared/models/post.dart';
import '../../shared/widgets/centered_content.dart';
import '../../shared/widgets/karar_avatar.dart';
import '../../shared/widgets/karar_button.dart';
import '../../shared/widgets/karar_chip.dart';
import '../../shared/widgets/karar_circle_button.dart';
import '../../shared/widgets/karar_surface.dart';
import '../../shared/widgets/karar_top_bar.dart';
import '../../shared/widgets/login_nudge.dart';
import '../../shared/widgets/on_color_chip.dart';
import '../../shared/widgets/post_surface.dart';
import '../feed/categories_provider.dart';
import 'create_post_provider.dart';
import 'drop_zone_widget.dart';
import 'image_picker_widget.dart';

class CreatePostScreen extends ConsumerStatefulWidget {
  const CreatePostScreen({super.key});

  @override
  ConsumerState<CreatePostScreen> createState() => _CreatePostScreenState();
}

class _CreatePostScreenState extends ConsumerState<CreatePostScreen> {
  final _titleController = TextEditingController();
  final _contentController = TextEditingController();
  bool _submitted = false;
  bool _acceptedPolicy = false;
  bool _contentStartedLogged = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) {
      ref.read(analyticsServiceProvider).logCreatePostStarted();
      _maybeRestoreDraft();
    });
  }

  Future<void> _maybeRestoreDraft() async {
    final draft = await ref.read(postDraftServiceProvider).loadDraft();
    if (draft == null || !mounted) return;

    final restore = await showDialog<bool>(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Kaydedilmiş taslak'),
        content: Text(
          'Daha önce yarım kalan bir yazı var:\n"${draft.title.length > 60 ? '${draft.title.substring(0, 60)}…' : draft.title}"\n\nDevam etmek ister misin?',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(ctx, false),
            child: const Text('Hayır, sil'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(ctx, true),
            child: const Text('Devam et'),
          ),
        ],
      ),
    );

    if (!mounted) return;
    if (restore == true) {
      _titleController.text = draft.title;
      _contentController.text = draft.content;
      if (draft.categoryId != null) {
        ref.read(createPostProvider.notifier).selectCategory(draft.categoryId!);
      }
      for (final tag in draft.tags) {
        ref.read(createPostProvider.notifier).addTag(tag);
      }
    } else {
      await ref.read(postDraftServiceProvider).clearDraft();
    }
  }

  Future<void> _saveDraft() async {
    final state = ref.read(createPostProvider);
    final draft = PostDraft(
      title: _titleController.text.trim(),
      content: _contentController.text.trim(),
      categoryId: state.selectedCategoryId,
      tags: state.tags,
    );
    await ref.read(postDraftServiceProvider).saveDraft(draft);
  }

  @override
  void dispose() {
    if (!_submitted && _titleController.text.isNotEmpty) {
      ref.read(analyticsServiceProvider).logCreatePostAbandoned();
    }
    _titleController.dispose();
    _contentController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (ref.read(currentUserProvider) == null) {
      LoginNudge.show(
        context,
        title: 'Durumunu Anlat',
        message:
            'Toplulukla bir durum paylaşmak ve karar vermelerini sağlamak için giriş yapmalısın.',
        returnTo: '/create',
      );
      return;
    }

    final title = _titleController.text.trim();
    final content = _contentController.text.trim();
    final state0 = ref.read(createPostProvider);
    final categoryName = state0.selectedCategoryId?.toString() ?? 'unknown';

    ref.read(analyticsServiceProvider).logCreatePostSubmitAttempted(
          category: categoryName,
          titleLength: title.length,
          contentLength: content.length,
          hasImage: state0.images.isNotEmpty,
        );

    if (title.length < 10) {
      ref
          .read(analyticsServiceProvider)
          .logCreatePostRejected(reason: 'title_too_short');
      _showError('Başlık en az 10 karakter olmalı.');
      return;
    }
    if (content.length < 50) {
      ref
          .read(analyticsServiceProvider)
          .logCreatePostRejected(reason: 'content_too_short');
      _showError('İçerik en az 50 karakter olmalı.');
      return;
    }

    if (!_acceptedPolicy) {
      ref
          .read(analyticsServiceProvider)
          .logCreatePostRejected(reason: 'policy_not_accepted');
      _showError('Kullanim kosullari ve topluluk kurallarini kabul etmelisin.');
      return;
    }

    final notifier = ref.read(createPostProvider.notifier);
    final success = await notifier.submit(
      title: title,
      content: content,
      acceptedTerms: _acceptedPolicy,
      acceptedCommunityGuidelines: _acceptedPolicy,
    );

    if (!mounted) return;

    final state = ref.read(createPostProvider);
    if (success) {
      _submitted = true;
      await ref.read(postDraftServiceProvider).clearDraft();
      if (!mounted) return;
      final category = state.selectedCategoryId?.toString() ?? 'unknown';
      final hasImage = state.images.isNotEmpty;
      ref.read(analyticsServiceProvider).logCreatePostPublished(
            category: category,
            hasImage: hasImage,
          );
      _titleController.clear();
      _contentController.clear();
      ref.read(notificationServiceProvider).maybeRequestPermission();
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Paylaşıldı! Topluluk kararını bekliyorsun.'),
        ),
      );
      context.go('/');
    } else if (state.error != null) {
      if (state.isDailyPostLimit) return;
      _showError(state.error!);
    }
  }

  void _showError(String message) {
    // Error snackbars use the Haksız fill with white text in both themes
    // (the dark `colorScheme.error` is a light coral that fails with white).
    final kc = context.kc;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          message,
          style: AppTypography.bodyS.copyWith(
            color: kc.onColor,
            fontWeight: FontWeight.w600,
          ),
        ),
        backgroundColor: kc.haksizFill,
        action: message.contains('paylaşılamadı')
            ? SnackBarAction(
                label: 'Tekrar Dene',
                textColor: kc.onColor,
                onPressed: _submit,
              )
            : null,
      ),
    );
  }

  void _selectCategory(Category category) {
    ref.read(createPostProvider.notifier).selectCategory(category.id);
    ref.read(analyticsServiceProvider).logCreatePostCategorySelected(
          category: category.name,
        );
  }

  @override
  Widget build(BuildContext context) {
    final state = ref.watch(createPostProvider);
    final categoriesAsync = ref.watch(categoriesProvider);
    final allCategories = categoriesAsync.valueOrNull ?? [];
    // Filter out "Hepsi" category (usually id 0)
    final postCategories = allCategories.where((c) => c.id != 0).toList();
    final currentUser = ref.watch(currentUserProvider);
    final kc = context.kc;
    // The body extends behind the app bar (frosted bar): content starts
    // below the status bar + toolbar.
    final barInset = MediaQuery.paddingOf(context).top +
        (Theme.of(context).appBarTheme.toolbarHeight ?? kToolbarHeight);

    Category? selectedCategory;
    for (final c in postCategories) {
      if (c.id == state.selectedCategoryId) selectedCategory = c;
    }
    final composeStyle = selectedCategory != null
        ? CategoryStyle.of(selectedCategory)
        : state.selectedCategoryId != null
            ? CategoryStyle.resolve(id: state.selectedCategoryId)
            : CategoryStyle.violet;

    return PopScope(
      canPop: false,
      onPopInvokedWithResult: (didPop, result) async {
        if (didPop) return;

        final title = _titleController.text.trim();
        final content = _contentController.text.trim();

        if (_submitted || (title.isEmpty && content.isEmpty)) {
          if (context.mounted) context.pop();
          return;
        }

        // 'save' → taslak kaydet ve çık, 'discard' → kaydetmeden çık, null → iptal
        final action = await showDialog<String>(
          context: context,
          builder: (ctx) => AlertDialog(
            title: const Text('Çıkmak istiyor musun?'),
            content: const Text('Yazmaya devam etmek için iptal edebilirsin.'),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(ctx),
                child: const Text('İptal'),
              ),
              TextButton(
                onPressed: () => Navigator.pop(ctx, 'discard'),
                style: TextButton.styleFrom(
                  foregroundColor: ctx.kc.haksizText,
                ),
                child: const Text('Sil, çık'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(ctx, 'save'),
                child: const Text('Taslağı kaydet'),
              ),
            ],
          ),
        );

        if (!context.mounted) return;
        if (action == 'save') {
          await _saveDraft();
          if (context.mounted) context.pop();
        } else if (action == 'discard') {
          await ref.read(postDraftServiceProvider).clearDraft();
          if (context.mounted) context.pop();
        }
      },
      child: Scaffold(
        // The form scrolls under a frosted bar (spec §2.8): canvas at rest,
        // canvas @ 88 % + blur + hairline once content is beneath it.
        extendBodyBehindAppBar: true,
        appBar: AppBar(
          centerTitle: false,
          backgroundColor: Colors.transparent,
          scrolledUnderElevation: 0,
          flexibleSpace: const KararTopBarBackground(),
          // The title lives in the same centred column as the form, so on
          // wide screens it lines up with the form's left edge instead of
          // sitting next to the side nav.
          titleSpacing: 0,
          title: CenteredContent(
            alignment: Alignment.center,
            child: SizedBox(
              width: double.infinity,
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: KararSpace.gutter,
                ),
                child: Text(
                  'Durumunu anlat',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: AppTypography.screenTitle.copyWith(color: kc.text1),
                ),
              ),
            ),
          ),
        ),
        body: state.isDailyPostLimit
            ? _DailyLimitView(
                topInset: barInset,
                onDone: () {
                  ref.read(createPostProvider.notifier).clearError();
                  context.go('/');
                },
              )
            : CenteredContent(
                child: ListView(
                  padding: EdgeInsets.fromLTRB(
                    0,
                    barInset + KararSpace.s8,
                    0,
                    context.navBottomInset + 24,
                  ),
                  children: [
                    // Category picker
                    if (postCategories.isNotEmpty) ...[
                      SizedBox(
                        height: 48,
                        child: ListView.separated(
                          scrollDirection: Axis.horizontal,
                          padding: const EdgeInsets.symmetric(
                            horizontal: KararSpace.gutter,
                          ),
                          itemCount: postCategories.length,
                          separatorBuilder: (_, __) =>
                              const SizedBox(width: KararSpace.s8),
                          itemBuilder: (context, index) {
                            final category = postCategories[index];
                            final style = CategoryStyle.of(category);
                            final selected =
                                state.selectedCategoryId == category.id;
                            return Center(
                              child: KararChip(
                                label: category.name,
                                icon: selected ? style.icon : style.iconOutlined,
                                selected: selected,
                                tone: KararChipTone.category(style),
                                onTap: () => _selectCategory(category),
                              ),
                            );
                          },
                        ),
                      ),
                      const SizedBox(height: KararSpace.s12),
                    ],

                    _Gutter(
                      child: _ComposeCard(
                        controller: _titleController,
                        style: composeStyle,
                        categoryName: selectedCategory?.name,
                        username: currentUser?.username,
                        anonymous: currentUser == null || state.isAnonymous,
                      ),
                    ),
                    const SizedBox(height: KararSpace.s16),

                    // Content
                    _Gutter(
                      child: _ContentField(
                        controller: _contentController,
                        onChanged: (text) {
                          setState(() {});
                          if (!_contentStartedLogged && text.isNotEmpty) {
                            _contentStartedLogged = true;
                            ref
                                .read(analyticsServiceProvider)
                                .logCreatePostContentStarted();
                          }
                          ref
                              .read(createPostProvider.notifier)
                              .checkToxicity(text);
                        },
                      ),
                    ),
                    if (state.toxicityScore >= 0.5)
                      const _Gutter(child: _ToxicityWarning()),
                    const SizedBox(height: KararSpace.s12),

                    // Image picker with web drag-drop support
                    _Gutter(
                      child: DropZoneWidget(
                        onFileDrop: (file) => ref
                            .read(createPostProvider.notifier)
                            .setDroppedImage(file),
                        onError: (error) => ref
                            .read(createPostProvider.notifier)
                            .setImageError(error),
                        child: ImagePickerWidget(
                          images: state.images,
                          onPick: () =>
                              ref.read(createPostProvider.notifier).pickImage(),
                          onRemove: (index) => ref
                              .read(createPostProvider.notifier)
                              .removeImage(index),
                          imageError: state.imageError,
                          permissionDenied: state.imagePermissionDenied,
                          uploadFailed: state.imageUploadFailed,
                          onRetry: state.imageUploadFailed ? _submit : null,
                        ),
                      ),
                    ),
                    const SizedBox(height: KararSpace.s16),

                    // Tag input
                    _Gutter(
                      child: _TagInput(
                        tags: state.tags,
                        onAdd: (tag) =>
                            ref.read(createPostProvider.notifier).addTag(tag),
                        onRemove: (tag) => ref
                            .read(createPostProvider.notifier)
                            .removeTag(tag),
                      ),
                    ),
                    const SizedBox(height: KararSpace.s20),

                    // Anonymous + unlisted toggles: rows run edge to edge
                    // inside the (clipping) surface, so hover/press fill
                    // the whole row instead of an inset tile.
                    _Gutter(
                      child: KararSurface(
                        radius: KararRadius.surface,
                        padding: EdgeInsets.zero,
                        child: Column(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            if (currentUser != null) ...[
                              _ToggleRow(
                                title: 'Anonim paylaş',
                                subtitle:
                                    'Adın gözükmez. Yorumlar her zaman kullanıcı adıyla gösterilir.',
                                value: state.isAnonymous,
                                onChanged: (val) => ref
                                    .read(createPostProvider.notifier)
                                    .setAnonymous(val),
                              ),
                              Divider(
                                height: 1,
                                indent: _ToggleRow.inset,
                                endIndent: _ToggleRow.inset,
                                color: kc.outlineVariant,
                              ),
                            ],
                            _ToggleRow(
                              title: 'Liste dışı paylaş',
                              subtitle:
                                  'Bu gönderi ana akışta görünmez, sadece paylaştığın linkle açılabilir.',
                              value: state.isUnlisted,
                              onChanged: (val) => ref
                                  .read(createPostProvider.notifier)
                                  .setUnlisted(val),
                            ),
                          ],
                        ),
                      ),
                    ),
                    const SizedBox(height: KararSpace.s12),

                    // Policy acceptance
                    _Gutter(
                      child: _PolicyTile(
                        value: _acceptedPolicy,
                        onChanged: state.isLoading
                            ? null
                            : (value) =>
                                setState(() => _acceptedPolicy = value),
                      ),
                    ),
                    const SizedBox(height: KararSpace.s20),

                    // Submit
                    _Gutter(
                      child: KararButton(
                        size: KararButtonSize.large,
                        icon: state.isLoading
                            ? Icons.hourglass_top_rounded
                            : Icons.send_rounded,
                        label: state.isLoading ? 'Paylaşılıyor...' : 'Karar Al',
                        onPressed:
                            state.isLoading || state.toxicityScore >= 0.8
                                ? null
                                : _submit,
                      ),
                    ),
                  ],
                ),
              ),
      ),
    );
  }
}

/// Horizontal page gutter for one form section.
class _Gutter extends StatelessWidget {
  const _Gutter({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: KararSpace.gutter),
      child: child,
    );
  }
}

/// Live preview of the post as a feed card (spec §6.4.3): the selected
/// category's gradient with the title typed straight onto it.
class _ComposeCard extends StatelessWidget {
  const _ComposeCard({
    required this.controller,
    required this.style,
    required this.categoryName,
    required this.username,
    required this.anonymous,
  });

  static const _maxTitle = 120;

  final TextEditingController controller;
  final CategoryStyle style;
  final String? categoryName;
  final String? username;
  final bool anonymous;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final onColor = context.kc.onColor;
    final showAnonymous = anonymous || username == null;
    final handle = showAnonymous ? '@anonim' : '@$username';

    return PostSurface.tinted(
      style: style,
      showRailSheen: false,
      child: ConstrainedBox(
        constraints: const BoxConstraints(minHeight: 230),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(
            KararSpace.cardPadding,
            KararSpace.s16,
            KararSpace.cardPadding,
            KararSpace.s16,
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ConstrainedBox(
                    constraints: const BoxConstraints(minHeight: 36),
                    child: Row(
                      children: [
                        KararAvatar(
                          username: username ?? '',
                          radius: 14,
                          anonymous: showAnonymous,
                          onColor: true,
                        ),
                        const SizedBox(width: KararSpace.s8),
                        Expanded(
                          child: Text(
                            handle,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: AppTypography.bodyS.copyWith(
                              color: onColor,
                              fontWeight: FontWeight.w700,
                            ),
                          ),
                        ),
                        if (categoryName != null) ...[
                          const SizedBox(width: KararSpace.s8),
                          OnColorChip(
                            icon: style.icon,
                            label: categoryName!,
                            maxWidth: 140,
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(height: KararSpace.s10),
                  Theme(
                    data: theme.copyWith(
                      textSelectionTheme: TextSelectionThemeData(
                        cursorColor: onColor,
                        selectionColor: onColor.withValues(alpha: 0.32),
                        selectionHandleColor: onColor,
                      ),
                    ),
                    child: TextField(
                      controller: controller,
                      maxLength: _maxTitle,
                      minLines: 2,
                      maxLines: null,
                      keyboardType: TextInputType.text,
                      textInputAction: TextInputAction.next,
                      textCapitalization: TextCapitalization.sentences,
                      // The title stays a single paragraph (as before the
                      // field became multi-line to wrap on the card).
                      inputFormatters: [
                        FilteringTextInputFormatter.deny(RegExp(r'[\r\n]')),
                      ],
                      cursorColor: onColor,
                      style: AppTypography.cardTitle.copyWith(
                        color: onColor,
                      ),
                      buildCounter: (_,
                              {required int currentLength,
                              required bool isFocused,
                              required int? maxLength}) =>
                          null,
                      decoration: InputDecoration(
                        hintText: 'Ne oldu kısaca?',
                        hintMaxLines: 2,
                        hintStyle: AppTypography.cardTitle.copyWith(
                          color: onColor,
                          fontWeight: FontWeight.w500,
                        ),
                        filled: false,
                        isDense: true,
                        contentPadding: EdgeInsets.zero,
                        border: InputBorder.none,
                        enabledBorder: InputBorder.none,
                        focusedBorder: InputBorder.none,
                        disabledBorder: InputBorder.none,
                        errorBorder: InputBorder.none,
                        focusedErrorBorder: InputBorder.none,
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: KararSpace.s8),
              ValueListenableBuilder<TextEditingValue>(
                valueListenable: controller,
                builder: (context, value, _) => Align(
                  alignment: Alignment.bottomRight,
                  child: Text(
                    '${value.text.characters.length}/$_maxTitle',
                    style: AppTypography.meta.copyWith(
                      color: onColor,
                      fontWeight: FontWeight.w700,
                      fontFeatures: AppTypography.tabular,
                    ),
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _ContentField extends StatelessWidget {
  const _ContentField({required this.controller, required this.onChanged});

  final TextEditingController controller;
  final ValueChanged<String> onChanged;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final nearLimit = controller.text.length >= 1400;
    OutlineInputBorder border(Color color, double width) => OutlineInputBorder(
          borderRadius: KararRadius.all(KararRadius.tile),
          borderSide: BorderSide(color: color, width: width),
        );

    return TextField(
      controller: controller,
      maxLength: 1500,
      minLines: 8,
      maxLines: 14,
      onChanged: onChanged,
      textCapitalization: TextCapitalization.sentences,
      decoration: InputDecoration(
        hintText: 'Durumu anlat... Diğer taraf ne yaptı?',
        alignLabelWithHint: true,
        contentPadding: const EdgeInsets.all(KararSpace.s16),
        // Theme field border: visible on the light canvas, hairline in dark.
        border: border(kc.fieldBorder, 1),
        enabledBorder: border(kc.fieldBorder, 1),
        focusedBorder: border(kc.primary, 2),
        counterStyle: AppTypography.meta.copyWith(
          color: nearLimit ? kc.haksizText : kc.text3,
          fontWeight: nearLimit ? FontWeight.w800 : null,
          fontFeatures: AppTypography.tabular,
        ),
      ),
    );
  }
}

class _ToxicityWarning extends StatelessWidget {
  const _ToxicityWarning();

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Semantics(
      liveRegion: true,
      child: Container(
        margin: const EdgeInsets.only(top: KararSpace.s8),
        padding: const EdgeInsets.symmetric(
          horizontal: KararSpace.s16,
          vertical: KararSpace.s12,
        ),
        decoration: BoxDecoration(
          color: kc.warnSoft,
          borderRadius: KararRadius.all(KararRadius.input),
        ),
        child: Row(
          children: [
            Icon(Icons.warning_amber_rounded, color: kc.warnText, size: 20),
            const SizedBox(width: KararSpace.s12),
            Expanded(
              child: Text(
                'Bu içerik topluluk kurallarına aykırı olabilir.',
                style: AppTypography.labelS.copyWith(
                  color: kc.warnText,
                  fontWeight: FontWeight.w600,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// One 64 px switch row of the toggles group. Square-cornered: the group
/// surface clips it, so the hover / press tint (brand 6 %) fills the row
/// edge to edge.
class _ToggleRow extends StatelessWidget {
  const _ToggleRow({
    required this.title,
    required this.subtitle,
    required this.value,
    required this.onChanged,
  });

  /// Horizontal inset of the row content (and of the divider between rows).
  static const inset = KararSpace.s20;

  final String title;
  final String subtitle;
  final bool value;
  final ValueChanged<bool> onChanged;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return ConstrainedBox(
      constraints: const BoxConstraints(minHeight: 64),
      child: SwitchListTile.adaptive(
        title: Text(
          title,
          style: AppTypography.bodyStrong.copyWith(
            fontSize: 16,
            color: kc.text1,
          ),
        ),
        subtitle: Padding(
          padding: const EdgeInsets.only(top: KararSpace.s2),
          child: Text(
            subtitle,
            style: AppTypography.bodyS.copyWith(color: kc.text2),
          ),
        ),
        value: value,
        onChanged: onChanged,
        contentPadding: const EdgeInsets.symmetric(
          horizontal: inset,
          vertical: KararSpace.s6,
        ),
        hoverColor: kc.primary.withValues(alpha: 0.06),
        shape: const RoundedRectangleBorder(),
      ),
    );
  }
}

/// Policy acceptance (spec §6.4.10): one radius-16 surface with the
/// checkbox row and, aligned under the first letter of its label, the two
/// policy links. The checkbox square lines up with the toggle rows' text.
class _PolicyTile extends StatelessWidget {
  const _PolicyTile({required this.value, required this.onChanged});

  final bool value;

  /// Null while a submit is in flight (checkbox disabled).
  final ValueChanged<bool>? onChanged;

  /// The checkbox is an 18 px square centred in a 40 px box (list tiles
  /// shrink-wrap it: the whole row is the tap target).
  static const _checkboxBox = 40.0;
  static const _checkboxSlack = (_checkboxBox - 18) / 2;

  /// Row start padding: the square's visual edge sits at the toggle inset.
  static const _start = _ToggleRow.inset - _checkboxSlack;

  /// Label start (11 px visual gap after the square).
  static const _labelStart = _start + _checkboxBox;

  /// TextButton horizontal padding (theme value, set explicitly below).
  static const _linkPadding = KararSpace.s12;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final theme = Theme.of(context);
    final linkStyle = TextButton.styleFrom(
      padding: const EdgeInsets.symmetric(
        horizontal: _linkPadding,
        vertical: KararSpace.s8,
      ),
      minimumSize: const Size(KararSpace.minTap, KararSpace.minTap),
      textStyle: AppTypography.labelS.copyWith(fontSize: 14, height: 20 / 14),
    );

    return KararSurface(
      radius: KararRadius.input,
      padding: EdgeInsets.zero,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Theme(
            data: theme.copyWith(
              hoverColor: kc.primary.withValues(alpha: 0.06),
            ),
            child: CheckboxListTile(
              value: value,
              onChanged: onChanged == null
                  ? null
                  : (checked) => onChanged!(checked ?? false),
              controlAffinity: ListTileControlAffinity.leading,
              materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
              contentPadding: const EdgeInsetsDirectional.only(
                start: _start,
                end: _ToggleRow.inset,
              ),
              horizontalTitleGap: 0,
              shape: const RoundedRectangleBorder(),
              title: Text(
                'Kullanım Koşulları ve Topluluk Kurallarını kabul ediyorum.',
                style: AppTypography.bodyS.copyWith(color: kc.text1),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsetsDirectional.only(
              start: _labelStart - _linkPadding,
              end: KararSpace.s8,
              bottom: KararSpace.s6,
            ),
            child: Wrap(
              children: [
                TextButton(
                  onPressed: () => context.push('/legal/terms'),
                  style: linkStyle,
                  child: const Text('Kullanım Koşulları'),
                ),
                // Community guidelines link
                TextButton(
                  onPressed: () => context.push('/legal/community'),
                  style: linkStyle,
                  child: const Text('Topluluk Kurallarını oku'),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// Daily post limit reached (spec §6.4.12): `EmptyState`-style disc.
class _DailyLimitView extends StatelessWidget {
  const _DailyLimitView({required this.onDone, required this.topInset});

  final VoidCallback onDone;

  /// Height of the app bar the body extends behind.
  final double topInset;

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    return Center(
      child: SingleChildScrollView(
        padding: EdgeInsets.fromLTRB(
          KararSpace.s32,
          topInset + KararSpace.s32,
          KararSpace.s32,
          context.navBottomInset + KararSpace.s32,
        ),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 320),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ExcludeSemantics(
                child: Container(
                  width: 88,
                  height: 88,
                  decoration: BoxDecoration(
                    color: kc.primaryContainer,
                    shape: BoxShape.circle,
                  ),
                  child: Icon(
                    Icons.hourglass_top_rounded,
                    size: 36,
                    color: kc.primary,
                  ),
                ),
              ),
              const SizedBox(height: KararSpace.s16),
              Semantics(
                header: true,
                child: Text(
                  'Günlük post limitine ulaştın',
                  textAlign: TextAlign.center,
                  style: AppTypography.headline.copyWith(color: kc.text1),
                ),
              ),
              const SizedBox(height: KararSpace.s8),
              Text(
                'Yarın tekrar paylaşabilirsin.',
                textAlign: TextAlign.center,
                style: AppTypography.body.copyWith(color: kc.text2),
              ),
              const SizedBox(height: KararSpace.s24),
              KararButton(
                label: 'Tamam',
                expand: false,
                onPressed: onDone,
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _PollInput extends StatefulWidget {
  const _PollInput({
    required this.options,
    required this.onAdd,
    required this.onUpdate,
    required this.onRemove,
    required this.onClear,
  });

  final List<String> options;
  final void Function(String) onAdd;
  final void Function(int, String) onUpdate;
  final void Function(int) onRemove;
  final VoidCallback onClear;

  @override
  State<_PollInput> createState() => _PollInputState();
}

class _PollInputState extends State<_PollInput> {
  final _optionCtrl = TextEditingController();

  @override
  void dispose() {
    _optionCtrl.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final hasPoll = widget.options.isNotEmpty;
    final canAdd = widget.options.length < CreatePostState.maxPollOptions;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Icon(Icons.poll_rounded, size: 20, color: kc.text2),
            const SizedBox(width: KararSpace.s8),
            Text(
              'Anket Ekle',
              style: AppTypography.bodyStrong.copyWith(color: kc.text1),
            ),
            const Spacer(),
            if (hasPoll)
              TextButton(
                onPressed: widget.onClear,
                style: TextButton.styleFrom(foregroundColor: kc.haksizText),
                child: const Text('Kaldır'),
              ),
          ],
        ),
        if (hasPoll) ...[
          const SizedBox(height: KararSpace.s8),
          for (int i = 0; i < widget.options.length; i++)
            Padding(
              padding: const EdgeInsets.only(bottom: KararSpace.s8),
              child: Row(
                children: [
                  Expanded(
                    child: TextField(
                      decoration: InputDecoration(
                        hintText: 'Seçenek ${i + 1}',
                      ),
                      onChanged: (text) => widget.onUpdate(i, text),
                      controller:
                          TextEditingController(text: widget.options[i]),
                    ),
                  ),
                  IconButton(
                    onPressed: () => widget.onRemove(i),
                    icon: const Icon(Icons.remove_circle_outline_rounded),
                    color: kc.haksizText,
                  ),
                ],
              ),
            ),
        ],
        if (canAdd)
          Row(
            children: [
              Expanded(
                child: TextField(
                  controller: _optionCtrl,
                  decoration: const InputDecoration(
                    hintText: 'Seçenek ekle...',
                  ),
                  onSubmitted: (_) => _add(),
                ),
              ),
              const SizedBox(width: KararSpace.s8),
              IconButton.filledTonal(
                onPressed: _add,
                icon: const Icon(Icons.add_rounded),
              ),
            ],
          ),
      ],
    );
  }

  void _add() {
    final text = _optionCtrl.text.trim();
    if (text.isNotEmpty) {
      widget.onAdd(text);
      _optionCtrl.clear();
    }
  }
}

class _TagInput extends StatefulWidget {
  const _TagInput({
    required this.tags,
    required this.onAdd,
    required this.onRemove,
  });

  final List<String> tags;
  final void Function(String) onAdd;
  final void Function(String) onRemove;

  @override
  State<_TagInput> createState() => _TagInputState();
}

class _TagInputState extends State<_TagInput> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _submit() {
    final text = _controller.text.trim();
    if (text.isNotEmpty) {
      widget.onAdd(text);
      _controller.clear();
    }
  }

  @override
  Widget build(BuildContext context) {
    final kc = context.kc;
    final canAdd = widget.tags.length < CreatePostState.maxTags;
    OutlineInputBorder pill(Color color, double width) => OutlineInputBorder(
          borderRadius: KararRadius.all(KararRadius.surface),
          borderSide: BorderSide(color: color, width: width),
        );

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Expanded(
              child: TextField(
                controller: _controller,
                enabled: canAdd,
                onSubmitted: (_) => _submit(),
                textInputAction: TextInputAction.done,
                decoration: InputDecoration(
                  // One fixed brand "#" at the content field's text start
                  // (16 padding + 4 outline gap = 20), in front of the hint
                  // and of the typed tag: no second "#" once focused.
                  hintText: canAdd
                      ? 'etiket ekle (maks. ${CreatePostState.maxTags})'
                      : 'Maksimum etiket sayısına ulaştın',
                  prefixIcon: canAdd
                      ? Padding(
                          padding: const EdgeInsetsDirectional.only(
                            start: KararSpace.s20,
                          ),
                          child: Text(
                            '#',
                            style: AppTypography.body.copyWith(
                              color: kc.primaryText,
                              fontWeight: FontWeight.w800,
                            ),
                          ),
                        )
                      : null,
                  prefixIconConstraints: const BoxConstraints(),
                  contentPadding: const EdgeInsets.symmetric(
                    horizontal: KararSpace.s16,
                    vertical: 14,
                  ),
                  border: pill(kc.fieldBorder, 1),
                  enabledBorder: pill(kc.fieldBorder, 1),
                  disabledBorder:
                      pill(kc.outlineVariant.withValues(alpha: 0.5), 1),
                  focusedBorder: pill(kc.primary, 2),
                ),
                maxLength: 20,
                buildCounter: (_,
                        {required int currentLength,
                        required bool isFocused,
                        required int? maxLength}) =>
                    null,
              ),
            ),
            if (canAdd) ...[
              const SizedBox(width: KararSpace.s10),
              KararCircleButton(
                icon: Icons.add_rounded,
                onPressed: _submit,
                tooltip: 'Ekle',
                style: KararCircleStyle.primary,
                size: 44,
                iconSize: 24,
              ),
            ],
          ],
        ),
        if (widget.tags.isNotEmpty) ...[
          const SizedBox(height: KararSpace.s8),
          Wrap(
            spacing: KararSpace.s8,
            children: widget.tags
                .map(
                  (tag) => KararChip(
                    label: '#$tag',
                    semanticLabel: '#$tag, Kaldır',
                    trailing: const Icon(Icons.close_rounded, size: 16),
                    onTap: () => widget.onRemove(tag),
                  ),
                )
                .toList(),
          ),
        ],
      ],
    );
  }
}
