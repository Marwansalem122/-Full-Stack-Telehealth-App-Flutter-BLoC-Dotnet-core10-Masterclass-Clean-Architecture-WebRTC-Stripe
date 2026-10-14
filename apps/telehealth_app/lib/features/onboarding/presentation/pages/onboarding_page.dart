
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:telehealth_app/app/theme/app_colors.dart';
import 'package:telehealth_app/features/onboarding/presentation/models/onboarding_page_data.dart';
import 'package:telehealth_app/l10n/app_localizations.dart';

class OnboardingPage extends StatefulWidget {
  const OnboardingPage({super.key});

  @override
  State<OnboardingPage> createState() => _OnboardingPageState();
}

class _OnboardingPageState extends State<OnboardingPage> {
  final PageController _pageController = PageController();

  int _currentPage = 0;

  List<OnboardingPageData> get _pages {
    final l10n = AppLocalizations.of(context)!;

    return [
      OnboardingPageData(
        title: l10n.onboardingTitle1,
        description: l10n.onboardingDescription1,
        imageAssetPath: 'assets/onboarding/onboarding1.png',
      ),
      OnboardingPageData(
        title: l10n.onboardingTitle2,
        description: l10n.onboardingDescription2,
        imageAssetPath: 'assets/onboarding/onboarding2.png',
      ),
      OnboardingPageData(
        title: l10n.onboardingTitle3,
        description: l10n.onboardingDescription3,
        imageAssetPath: 'assets/onboarding/onboarding3.png',
      ),
      OnboardingPageData(
        title: l10n.onboardingTitle4,
        description: l10n.onboardingDescription4,
        imageAssetPath: 'assets/onboarding/onboarding4.png',
      ),
    ];
  }

  bool get _isLastPage => _currentPage == _pages.length - 1;

  void _nextPage() {
    if (_isLastPage) {
      _finishOnboarding();
      return;
    }

    _pageController.nextPage(
      duration: const Duration(milliseconds: 350),
      curve: Curves.easeInOut,
    );
  }

  void _skipOnboarding() {
    _pageController.animateToPage(
      _pages.length - 1,
      duration: const Duration(milliseconds: 300),
      curve: Curves.easeInOut,
    );
  }

  void _finishOnboarding() {
    context.go('/login');
  }

  @override
  void dispose() {
    _pageController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final l10n = AppLocalizations.of(context)!;

    return Scaffold(
      body: SafeArea(
        child: Column(
          children: [
            Expanded(
              child: PageView.builder(
                controller: _pageController,
                itemCount: _pages.length,
                onPageChanged: (index) {
                  setState(() => _currentPage = index);
                },
                itemBuilder: (context, index) {
                  final page = _pages[index];

                  return Column(
                    // mainAxisAlignment: MainAxisAlignment.center,
                    children: [
                    SizedBox(
                      height: 450,
                      width: double.infinity,
                      child: Image.asset(
                        page.imageAssetPath,
                        fit: BoxFit.cover,
                        width: double.infinity,
                        errorBuilder: (context, error, stackTrace) {
                          return const Icon(
                            Icons.image_not_supported_outlined,
                            size: 100,
                          );
                        },
                      ),
                    ),
                      const SizedBox(height: 24),
                      Text(
                        page.title,
                        textAlign: TextAlign.center,
                        
                        style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                              fontWeight: FontWeight.bold,
                               color:AppColors.primary,
                            ),
                      ),
                      const SizedBox(height: 16),
                      Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 24),
                        child: Text(
                          page.description,
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                                fontSize: 16,
                              ),
                        ),
                      ),
                      // const SizedBox(height: 24),
                    ],
                  );
                },
              ),
            ),
            Row(
              mainAxisAlignment: MainAxisAlignment.center,
              children: List.generate(
                _pages.length,
                (index) => AnimatedContainer(
                  duration: const Duration(milliseconds: 250),
                  margin: const EdgeInsets.symmetric(horizontal: 4),
                  width: _currentPage == index ? 24 : 8,
                  height: 8,
                  decoration: BoxDecoration(
                    color: _currentPage == index
                        ? Theme.of(context).colorScheme.primary
                        : Theme.of(context).colorScheme.outlineVariant,
                    borderRadius: BorderRadius.circular(8),
                  ),
                ),
              ),
            ),
            const SizedBox(height: 32),
          Padding(
  padding: const EdgeInsets.symmetric(horizontal: 10),
  child: Row(
    children: [
      if (!_isLastPage) ...[
        TextButton(
          onPressed: _skipOnboarding,
          child: Text(l10n.skip),
        ),
        const Spacer(),
      ],
      Expanded(
        child: FilledButton(
          style: FilledButton.styleFrom(
            backgroundColor: AppColors.primary,
           shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(12),
            ),
          ),
          onPressed: _nextPage,
          child: Row(
            mainAxisAlignment: MainAxisAlignment.center,
            children: [
              Text(
                _isLastPage ? l10n.getStarted : l10n.next,
              ),
              const SizedBox(width: 8),
              const Icon(Icons.arrow_forward),
            ],
          ),
        ),
      ),
    ],
  ),
),   ],
        ),
      ),
    );
  }
}
