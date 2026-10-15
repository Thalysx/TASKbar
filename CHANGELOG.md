# TaskBAR

## 0.3.0 · Menu compacto na bandeja

- Inicialização diretamente na bandeja, com menu escuro e seis perfis de aparência.
- Efeito, cor, opacidade, Peek e opções compatíveis com a versão do Windows acessíveis pelos submenus.
- Presets, regras, JSON e editor completo disponíveis em Avançado; a janela só é criada quando solicitada.
- Controle de inicialização com o Windows no próprio menu.
- Configuração e inicialização existentes preservadas na atualização.
- 46 verificações do motor, seis verificações do menu e duas do editor avançado aprovadas.

## 0.2.1 · Inicialização com o Windows

- Modo `--startup`: janela oculta, bandeja disponível e ativação dos perfis após a barra estar disponível.
- Controle de inicialização automática em Sobre o TaskBAR.
- Execuções duplicadas no login são ignoradas sem abrir aviso.
- Instalação fixa no perfil do usuário, preservando a configuração da versão em uso.
- 46 verificações automáticas e teste de preparação da inicialização pela bandeja aprovados.

## 0.2.0

## Personalização

- Controle de opacidade de 0 a 100%, sincronizado com o alfa hexadecimal e a prévia.
- Presets Transparente, Vidro fosco, Acrílico discreto, Sólido escuro e Adaptativo. Preservam regras e exclusões existentes.
- Editor visual para adicionar, editar e remover regras por processo, classe e trecho do título.
- Aparência inativa opcional para regras de janelas maximizadas.
- Edições JSON pendentes são preservadas ao navegar; o editor permite descartá-las explicitamente.

## Desempenho e estabilidade

- Uma enumeração de janelas por captura, incluindo a localização das barras.
- Cache de nomes de processos por 2 segundos e de backend por HWND/PID, invalidado quando o processo do Explorer muda.
- Eventos de janelas antecipam as capturas; eventos em sequência são agrupados. Na ausência de eventos, varreduras a cada 2 segundos com aplicativo ativo/janela visível e 5 segundos quando pausado e oculto.
- Consultas de conflito usam cache curto na observação; a verificação imediatamente antes de renderizar permanece sem cache.
- Pausar ou sair invalida operações pendentes, incluindo uma conexão XAML em andamento.
- Quando o PID do Explorer muda, descarta handles, estilos e estado de renderização antigos antes de reconectar.
- Testes de interface usam configuração isolada, sem gravar nas configurações do usuário.

## Verificação

- 45 verificações do motor, parser, presets, opacidade, regras, agenda de varredura e cancelamento: aprovadas.
- Inicialização, bandeja, navegação, opacidade, gravação/remoção de regras e proteção de JSON pendente verificadas na sessão real.
- Medição local: 30 capturas em aproximadamente 24 ms, com duas consultas de nomes de processos para duas janelas elegíveis. É uma medição pontual, não uma promessa de consumo de CPU.
- Interface de perfis e de regras renderizada e inspecionada.
- O backend XAML e a reinicialização real do Explorer não foram exercitados nesta máquina Windows 10.
